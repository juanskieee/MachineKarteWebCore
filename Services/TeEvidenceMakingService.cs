using GemBox.Spreadsheet;
using MachineKarte.Reporting;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services.Reporting;

public class TeEvidenceMakingService
{
    private readonly TeEvidenceRepository _repo;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public TeEvidenceMakingService(TeEvidenceRepository repo, IConfiguration config, IWebHostEnvironment env)
        => (_repo, _config, _env) = (repo, config, env);

    public async Task<string> GenerateAsync(string bmId, string process,
        CancellationToken cancellationToken = default, string format = "xlsx")
    {
        format = LegacyReportExport.NormalizeFormat(format);
        process = LegacyReportExport.NormalizeProcess(process);
        if (!int.TryParse(bmId, out var id) || id <= 0) throw new ArgumentException("A valid BM ID is required.");
        var headerTask = MonitoringQueryBatch.RunAsync(() => _repo.GetHeaderAsync(bmId, cancellationToken), cancellationToken);
        var assignTask = MonitoringQueryBatch.RunAsync(() => _repo.GetTEAssignAsync(bmId, cancellationToken), cancellationToken);
        var descriptionsTask = MonitoringQueryBatch.RunAsync(() => _repo.GetCategoryDescsAsync(bmId, cancellationToken), cancellationToken);
        var imagesTask = MonitoringQueryBatch.RunAsync(() => _repo.GetCategoryImageRowsAsync(bmId, cancellationToken), cancellationToken);
        var telekoTask = MonitoringQueryBatch.RunAsync(() => _repo.GetTelekoImagesAsync(bmId, cancellationToken), cancellationToken);
        var qualityTask = MonitoringQueryBatch.RunAsync(() => _repo.GetQualityImagesAsync(bmId, cancellationToken), cancellationToken);
        var miscTask = MonitoringQueryBatch.RunAsync(() => _repo.GetMiscRowsAsync(bmId, cancellationToken), cancellationToken);
        await Task.WhenAll(headerTask, assignTask, descriptionsTask, imagesTask, telekoTask, qualityTask, miscTask);
        cancellationToken.ThrowIfCancellationRequested();
        var header = await headerTask ?? throw new InvalidDataException($"No TE machine data found for BM ID {id}.");
        header.TEAssign = await assignTask;
        var workbook = LegacyReportExport.Load(Path.Combine(_env.ContentRootPath, "Templates", "EvidenceMaking", "CWC.xlsx"));
        PopulateWorkbook(workbook, header, await descriptionsTask, await imagesTask, await telekoTask, await qualityTask,
            LegacyReportExport.ImageRoot(_env, true),
            LegacyReportExport.LegacyEvidenceRoot(_config, true), await miscTask,
            LegacyReportExport.SharedEvidenceRoot(_config));
        var fileName = $"TE_Evidence_{id}.{format}";
        await LegacyReportExport.SaveReportAsync(_config, true, process, fileName,
            LegacyReportExport.Save(workbook, format), cancellationToken);
        return $"/TeEvidenceMaking?handler=Download&process={process}&bmId={id}&format={format}";
    }

    public static void PopulateWorkbook(ExcelFile workbook, TeEvidenceHeader header,
        List<TeEvidenceCategoryDesc> descriptions, List<TeEvidenceImage> images,
        List<TelekoEvidenceImage> teleko, List<QualityConditionImage> quality, string imageRoot, string legacySystemRoot,
        IList<IDictionary<string, object>>? miscRows = null, string? sharedEvidenceRoot = null)
    {
        // Fail explicitly when a record requires a sheet absent from the supplied template.
        // Never silently discard board connectors or quality-condition data.
        if (!workbook.Worksheets.Any(sheet => sheet.Name == "TESTBOARD") && images.Any(image => image.Category == "TEST BOARD" &&
            image.CN is "CN5" or "CN6" or "CN7" or "CN8"))
            throw new InvalidDataException("CWC.xlsx has no TESTBOARD worksheet required by this record's CN5-CN8 test-board evidence.");
        if (!workbook.Worksheets.Any(sheet => sheet.Name == "DATASHEET") && (quality.Count > 0 ||
            descriptions.Any(row => row.Category is "BEFORE QUALITY CONDITION" or "AFTER QUALITY CONDITION" or "IDEAL QUALITY CONDITION")))
            throw new InvalidDataException("CWC.xlsx has no DATASHEET worksheet required by this record's quality-condition evidence.");
        IList<IDictionary<string, object>> Rows(IEnumerable<Dictionary<string, object>> source)
            => source.Cast<IDictionary<string, object>>().ToList();
        string Image(string path) => LegacyReportExport.ResolveEvidenceImage(path, legacySystemRoot, imageRoot,
            te: true, sharedEvidenceRoot: sharedEvidenceRoot);
        var rows = new Dictionary<string, IList<IDictionary<string, object>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["DR"] = Rows(new[] { new Dictionary<string, object> {
                ["MCNo"]=header.MCNo, ["TesterNo"]=header.TesterNo, ["DeviceName"]=header.DeviceName,
                ["LOTNo"]=header.LotNo, ["TestNoNG"]=header.TestNoNG, ["StartOfRepair"]=header.StartOfRepair,
                ["EndOfRepair"]=header.EndOfRepair } }),
            ["emDRCat"] = Rows(descriptions.Select(row => new Dictionary<string, object> {
                ["BMEvidence_Category"]=row.Category, ["Condition"]=row.Condition, ["BMEvidence_Desc"]=row.Desc })),
            ["emDR_Data"] = miscRows ?? Rows(new[] { new Dictionary<string, object> {
                ["Ideas"]=header.Ideas, ["WaferNo"]=header.WaferNo, ["Remarks"]=header.Remarks,
                ["TelekoCheckingData"]=header.TelekoCheckingData } }),
            ["emDR_DataTeleko"] = Rows(teleko.Select(row => new Dictionary<string, object> { ["TelekoEvidence"]=Image(row.TelekoEvidence) }))
        };
        var categories = new[] {"PHENOMENON","MACHINE COUNTER AND VISUAL BIN","PROGRAM LOADED","TEST BOARD","TEST CABLE","OTHER EVIDENCE"};
        for (var i=0; i<categories.Length; i++)
        {
            var categoryImages = images.Where(row => row.Category == categories[i]);
            if (categories[i] == "TEST BOARD")
                // The legacy loop leaves the last ordered row visible at each connector.
                categoryImages = categoryImages.GroupBy(row => (row.Condition, row.CN)).Select(group => group.Last());
            rows[$"emDR{i+1}"] = Rows(categoryImages.Select(row => new Dictionary<string, object> {
                ["Condition"]=row.Condition, ["CN"]=row.CN, ["BMEvidence_ImgFile"]=Image(row.ImgFile) }));
        }
        var qualityCategories = new[] {"BEFORE QUALITY CONDITION","AFTER QUALITY CONDITION","IDEAL QUALITY CONDITION"};
        for (var i=0; i<qualityCategories.Length; i++)
            rows[$"emDR{i+1}_QD"] = Rows(quality.Where(row => row.Category==qualityCategories[i])
                .Select(row => new Dictionary<string, object> {["BMEvidence_ImgFile"]=Image(row.ImgFile)}));
        new LegacyReportWriter(imageRoot, new Dictionary<string, string>())
            .PopulateTeEvidence(workbook, rows, header.TEAssign);
    }
}
