using MachineKarte.Reporting;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services;

public class BmReportService
{
    private readonly BmReportRepository _repo;
    private readonly IWebHostEnvironment _env;

    public BmReportService(BmReportRepository repo, IWebHostEnvironment env, ILogger<BmReportService> log)
        => (_repo, _env) = (repo, env);

    public async Task<(byte[] bytes, string fileName)> GenerateAsync(
        int bmId, string process, CancellationToken cancellationToken = default, string format = "xlsx")
    {
        format = LegacyReportExport.NormalizeFormat(format);
        var data = await _repo.GetReportDataAsync(bmId, cancellationToken)
            ?? throw new InvalidOperationException($"No record found for bmID={bmId}");
        await _repo.LoadSupplementalDataAsync(data, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var template = process == "EDS" ? "EDS-CSPTemplate.xlsx" : "TETemplate.xlsx";
        var workbook = LegacyReportExport.Load(Path.Combine(_env.ContentRootPath, "Templates", "BmReport", template));
        var writer = new LegacyReportWriter(LegacyReportExport.ImageRoot(_env, true),
            data.LookupValues, new[] { data.VRNumber1, data.VRNumber2, data.VRNumber3 });
        writer.PopulateTe(workbook, process, data.Values);
        return (LegacyReportExport.Save(workbook, format), $"TE_{bmId}.{format}");
    }
}
