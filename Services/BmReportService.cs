using MachineKarte.Reporting;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services;

public class BmReportService
{
    private readonly BmReportRepository _repo;
    private readonly IWebHostEnvironment _env;
    public BmReportService(BmReportRepository repo, IWebHostEnvironment env)
        => (_repo, _env) = (repo, env);

    public async Task<(byte[] bytes, string fileName)> GenerateAsync(
        int bmId, string process, CancellationToken cancellationToken = default, string format = "xlsx")
    {
        format = LegacyReportExport.NormalizeFormat(format);
        var dataTask = _repo.GetReportDataAsync(bmId, cancellationToken);
        var template = process == "EDS" ? "EDS-CSPTemplate.xlsx" : "TETemplate.xlsx";
        var workbook = LegacyReportExport.Load(Path.Combine(_env.ContentRootPath, "Templates", "BmReport", template));
        var data = await dataTask
            ?? throw new InvalidOperationException($"No record found for bmID={bmId}");
        await _repo.LoadSupplementalDataAsync(data, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var writer = new LegacyReportWriter(LegacyReportExport.ImageRoot(_env, true),
            data.LookupValues, new[] { data.VRNumber1, data.VRNumber2, data.VRNumber3 });
        writer.PopulateTe(workbook, process, data.Values);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = LegacyReportExport.Save(workbook, format);
        return (bytes, $"TE_{bmId}.{format}");
    }
}
