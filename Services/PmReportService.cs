using MachineKarte.Reporting;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services;

public sealed class PmReportService
{
    private readonly PmReportRepository _repository;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PmReportService> _logger;

    public PmReportService(PmReportRepository repository, IWebHostEnvironment environment, ILogger<PmReportService> logger)
        => (_repository, _environment, _logger) = (repository, environment, logger);

    public async Task<PmReportResult> GenerateAsync(PmReportRequest request, CancellationToken cancellationToken = default)
    {
        var process = request.Process.Trim().ToUpperInvariant();
        if (!PmReportRepository.TryGetDefinition(process, out var definition))
            return PmReportResult.Failed($"Unsupported PM report process: {request.Process}");
        if (request.BmId <= 0) return PmReportResult.Failed("A valid BM ID is required.");

        try
        {
            var format = LegacyReportExport.NormalizeFormat(request.Format);
            var data = await _repository.GetReportDataAsync(process, request.BmId,
                request.TimeOfRequest, request.MachineNo, cancellationToken);
            if (data is null) return PmReportResult.Failed($"No PM report record was found for BM ID {request.BmId}.");
            var lookups = await _repository.GetLookupValuesAsync(data, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var workbook = LegacyReportExport.Load(Path.Combine(_environment.ContentRootPath, "Templates", "PmReport", definition.TemplateFile));
            var writer = new LegacyReportWriter(LegacyReportExport.ImageRoot(_environment, false), lookups);
            writer.PopulatePm(workbook, process, data.Current.Values,
                data.History.Select(record => (IDictionary<string, object>)record.Values).ToList());
            var bytes = LegacyReportExport.Save(workbook, format);
            var directory = Path.Combine(_environment.ContentRootPath, "TempData", "GeneratedPdf");
            Directory.CreateDirectory(directory);
            var fileName = $"{process}_{request.BmId}.{format}";
            await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes, cancellationToken);
            return new PmReportResult
            {
                Success = true, Message = "PM report generated.", FileName = fileName,
                DownloadUrl = $"/PmReport?handler=Download&fileName={Uri.EscapeDataString(fileName)}"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "PM report generation failed for {Process}/{BmId}", process, request.BmId);
            return PmReportResult.Failed("PM report generation failed. " + ex.Message);
        }
    }
}
