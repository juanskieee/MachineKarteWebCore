using GemBox.Spreadsheet;
using MachineKarte.Reporting;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services.Reporting;

public class EvidenceMakingService
{
    private readonly EvidenceRepository _repo;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EvidenceMakingService> _logger;

    public EvidenceMakingService(EvidenceRepository repo, IConfiguration config,
        IWebHostEnvironment env, ILogger<EvidenceMakingService> logger)
        => (_repo, _config, _env, _logger) = (repo, config, env, logger);

    public async Task<EvidenceGenerateResult> GenerateEvidenceAsync(
        EvidenceGenerateRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = ProcessTemplateMap.Get(request.Process);
        if (cfg is null) return Fail($"Unknown process: {request.Process}");
        try
        {
            var format = LegacyReportExport.NormalizeFormat(request.Format);
            var imagesTask = MonitoringQueryBatch.RunAsync(
                () => _repo.GetEvidenceImagesAsync(request.BmId, request.Process, cancellationToken), cancellationToken);
            var tscTask = MonitoringQueryBatch.RunAsync(
                () => _repo.GetTscConfirmationAsync(request.BmId, request.Process, cfg.EvidenceView, cancellationToken), cancellationToken);
            var headerTask = MonitoringQueryBatch.RunAsync(
                () => _repo.GetMachineHeaderAsync(request.BmId, cfg.DataView, cancellationToken), cancellationToken);
            await Task.WhenAll(imagesTask, tscTask, headerTask);
            var header = await headerTask;
            if (header is null) return Fail($"No machine data found for BM ID {request.BmId}");
            cancellationToken.ThrowIfCancellationRequested();
            var workbook = LegacyReportExport.Load(Path.Combine(_env.ContentRootPath, "Templates", "EvidenceMaking", cfg.TemplateFile));
            PopulateWorkbook(workbook, request.Process, header, await tscTask, await imagesTask,
                LegacyReportExport.ImageRoot(_env, false), LegacyReportExport.LegacyEvidenceRoot(_config, false),
                LegacyReportExport.SharedEvidenceRoot(_config));
            var fileName = $"{request.Process}Evidence_{request.BmId}.{format}";
            var process = LegacyReportExport.NormalizeProcess(request.Process);
            await LegacyReportExport.SaveReportAsync(_config, false, process, fileName,
                LegacyReportExport.Save(workbook, format), cancellationToken);
            return new EvidenceGenerateResult
            {
                Success = true, FileName = fileName,
                DownloadUrl = $"/EvidenceMaking?handler=Download&process={process}&fileName={Uri.EscapeDataString(fileName)}"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Evidence generation failed for BM {BmId}", request.BmId);
            return Fail("Evidence generation failed. " + ex.Message);
        }
    }

    public static void PopulateWorkbook(ExcelFile workbook, string process, EvidenceMachineHeader h,
        TscConfirmation t, EvidenceImageGroups images, string imageRoot, string legacySystemRoot,
        string? sharedEvidenceRoot = null)
    {
        var header = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Andon"]=h.Andon,
            ["MCNo"]=h.McNo, ["MCType"]=h.McType, ["PkgName"]=h.PkgName, ["DeviceName"]=h.DeviceName,
            ["LOTNo"]=h.LotNo, ["PMConfirmGLOPReqBy"]=h.PmConfirmBy, ["OccurrenceDate"]=h.OccurrenceDate,
            ["StartOfRepair"]=h.StartOfRepair, ["EndOfRepair"]=h.EndOfRepair
        };
        var confirmation = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["BMRouteNo"]=t.RouteNo, ["BMVersionNo"]=t.VersionNo,
            ["SignalTowerKanban"]=t.SignalTowerKanban, ["SafetyTags"]=t.SafetyTags, ["Bypass"]=t.Bypass
        };
        var flags = new[] {t.Tsc1,t.Tsc2,t.Tsc3,t.Tsc4,t.Tsc5,t.Tsc6,t.Tsc7,t.Tsc8,t.Tsc9};
        for (var i=0; i<flags.Length; i++) confirmation[$"BMEvidence_TCConfirm{i+1}"]=flags[i];
        var groups = new[] {images.ProblemPoint,images.ActionTaken,images.PartsReplaced,images.DisAssembly,images.QualityConfirm};
        var paths = groups.Select(group => group.Select(row =>
            LegacyReportExport.ResolveEvidenceImage(row.ImageFile, legacySystemRoot, imageRoot,
                sharedEvidenceRoot: sharedEvidenceRoot)).ToArray()).ToArray();
        var descriptions = groups.Select(group => group.Select(row => row.Description).ToArray()).ToArray();
        new LegacyReportWriter(imageRoot, new Dictionary<string,string>())
            .PopulateEvidence(workbook, process, header, confirmation, paths, descriptions);
    }

    private static EvidenceGenerateResult Fail(string message) => new() { Success=false, Message=message };
}
