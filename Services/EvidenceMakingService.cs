using GemBox.Spreadsheet;
using MachineKarte.Reporting;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services;

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
            // These fixed reads must not queue behind dashboard refreshes.
            var imagesTask = _repo.GetEvidenceImagesAsync(request.BmId, request.Process, cancellationToken);
            var tscTask = _repo.GetTscConfirmationAsync(request.BmId, request.Process, cfg.EvidenceView, cancellationToken);
            var headerTask = _repo.GetMachineHeaderAsync(request.BmId, cfg.DataView, cancellationToken);
            var dataTask = Task.WhenAll(imagesTask, tscTask, headerTask);
            var workbook = LegacyReportExport.Load(Path.Combine(_env.ContentRootPath, "Templates", "EvidenceMaking", cfg.TemplateFile));
            // Start share reads as soon as image rows arrive, while header/confirmation SQL continues.
            var images = await imagesTask;
            var groups = new[] { images.ProblemPoint, images.ActionTaken, images.PartsReplaced, images.DisAssembly, images.QualityConfirm };
            using var photos = await LegacyReportExport.StagedPhotos.LoadAsync(
                groups.SelectMany(group => group).Select(row => row.ImageFile),
                LegacyReportExport.LegacyEvidenceRoot(_config, false), LegacyReportExport.ImageRoot(_env, false),
                false, LegacyReportExport.SharedEvidenceRoot(_config), cancellationToken, _config);
            await dataTask;
            var header = await headerTask;
            if (header is null) return Fail($"No machine data found for BM ID {request.BmId}");
            cancellationToken.ThrowIfCancellationRequested();
            var confirmation = await tscTask;
            PopulateWorkbook(workbook, request.Process, header, confirmation, images,
                LegacyReportExport.ImageRoot(_env, false), LegacyReportExport.LegacyEvidenceRoot(_config, false),
                LegacyReportExport.SharedEvidenceRoot(_config), photos.Paths);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = LegacyReportExport.Save(workbook, format);
            var fileName = $"{request.Process}Evidence_{request.BmId}.{format}";
            return new EvidenceGenerateResult
            {
                Success = true, FileName = fileName,
                Bytes = bytes
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
        string? sharedEvidenceRoot = null, IReadOnlyDictionary<string, string>? imagePaths = null)
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
        var paths = groups.Select(group => group.Select(row => imagePaths is not null ? imagePaths[row.ImageFile] :
            LegacyReportExport.ResolveEvidenceImage(row.ImageFile, legacySystemRoot, imageRoot,
                sharedEvidenceRoot: sharedEvidenceRoot)).ToArray()).ToArray();
        var descriptions = groups.Select(group => group.Select(row => row.Description).ToArray()).ToArray();
        new LegacyReportWriter(imageRoot, new Dictionary<string,string>())
            .PopulateEvidence(workbook, process, header, confirmation, paths, descriptions);
    }

    private static EvidenceGenerateResult Fail(string message) => new() { Success=false, Message=message };
}
