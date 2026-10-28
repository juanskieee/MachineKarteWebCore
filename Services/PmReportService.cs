using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services;

public sealed class PmReportService
{
    private static readonly Regex SourceLine = new(
        @"^\s*\d+:\s*(?<code>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CellExpression = new(
        @"ws\.Cells\(""(?<cell>[^""]+)""\)(?<operation>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FieldReference = new(
        @"(?:DR|DRss(?<history>[12]))\(""(?<field>[^""]+)""\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PmReportRepository _repository;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PmReportService> _logger;

    public PmReportService(
        PmReportRepository repository,
        IWebHostEnvironment environment,
        ILogger<PmReportService> logger)
    {
        _repository = repository;
        _environment = environment;
        _logger = logger;
    }

    public async Task<PmReportResult> GenerateAsync(
        PmReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var process = request.Process.Trim().ToUpperInvariant();
        if (!PmReportRepository.TryGetDefinition(process, out var definition))
            return PmReportResult.Failed($"Unsupported PM report process: {request.Process}");

        if (request.BmId <= 0)
            return PmReportResult.Failed("A valid BM ID is required.");

        var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "PmReport", definition.TemplateFile);
        if (!File.Exists(templatePath))
            return PmReportResult.Failed($"PM report template not found: {definition.TemplateFile}");

        PmReportData? data;
        try
        {
            data = await _repository.GetReportDataAsync(
                process,
                request.BmId,
                request.TimeOfRequest,
                request.MachineNo,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to read PM report data for {Process}/{BmId}", process, request.BmId);
            return PmReportResult.Failed("Unable to read PM report data.");
        }

        if (data is null)
            return PmReportResult.Failed($"No PM report record was found for BM ID {request.BmId}.");

        var matrixPath = Path.Combine(_environment.ContentRootPath, "Form3CellMappingMatrix.txt");
        if (!File.Exists(matrixPath))
            return PmReportResult.Failed("Form3CellMappingMatrix.txt is required for PM report generation.");

        try
        {
            using var workbook = new XLWorkbook(templatePath);
            var worksheet = ResolveWorksheet(workbook, definition.WorksheetName);
            var mapping = ReadWriterLines(matrixPath, process + "Data");
            ApplyMapping(worksheet, mapping, data);

            var outputDirectory = Path.Combine(_environment.ContentRootPath, "TempData", "GeneratedPdf");
            Directory.CreateDirectory(outputDirectory);
            var fileName = $"{process}_{request.BmId}.xlsx";
            var outputPath = Path.Combine(outputDirectory, fileName);
            workbook.SaveAs(outputPath);

            return new PmReportResult
            {
                Success = true,
                Message = "PM report generated.",
                FileName = fileName,
                DownloadUrl = $"/PmReport?handler=Download&fileName={Uri.EscapeDataString(fileName)}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to generate PM report for {Process}/{BmId}", process, request.BmId);
            return PmReportResult.Failed("PM report generation failed. Check the server logs.");
        }
    }

    private static IXLWorksheet ResolveWorksheet(XLWorkbook workbook, string worksheetName)
    {
        var worksheet = workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, worksheetName, StringComparison.OrdinalIgnoreCase));
        return worksheet ?? workbook.Worksheets.First();
    }

    private static List<string> ReadWriterLines(string path, string writerName)
    {
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, line => line.Trim().Equals($"# {writerName}", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            throw new InvalidOperationException($"Writer mapping '{writerName}' was not found.");

        var end = lines.Length;
        for (var index = start + 1; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("# ", StringComparison.Ordinal))
            {
                end = index;
                break;
            }
        }

        return lines[start..end].ToList();
    }

    private static void ApplyMapping(
        IXLWorksheet worksheet,
        IEnumerable<string> lines,
        PmReportData data)
    {
        foreach (var line in lines)
        {
            var source = SourceLine.Match(line);
            if (!source.Success) continue;

            var code = source.Groups["code"].Value.Trim();
            if (code.StartsWith("'", StringComparison.Ordinal)) continue;

            var cellMatch = CellExpression.Match(code);
            if (!cellMatch.Success) continue;

            var cell = worksheet.Cell(cellMatch.Groups["cell"].Value);
            var operation = cellMatch.Groups["operation"].Value.Trim();

            if (operation.Contains("FillPattern.SetSolid", StringComparison.OrdinalIgnoreCase))
            {
                cell.Style.Fill.SetBackgroundColor(XLColor.Red);
                continue;
            }

            if (operation.Contains("UnderlineStyle", StringComparison.OrdinalIgnoreCase))
            {
                cell.Style.Font.Underline = XLFontUnderlineValues.Single;
                continue;
            }

            if (operation.Contains("Font.Weight", StringComparison.OrdinalIgnoreCase))
            {
                cell.Style.Font.Bold = true;
                continue;
            }

            var valueIndex = operation.IndexOf(".Value", StringComparison.OrdinalIgnoreCase);
            var formulaIndex = operation.IndexOf(".Formula", StringComparison.OrdinalIgnoreCase);
            var assignmentIndex = valueIndex >= 0 ? valueIndex : formulaIndex;
            if (assignmentIndex < 0) continue;

            var equalsIndex = operation.IndexOf('=', assignmentIndex);
            if (equalsIndex < 0) continue;

            var expression = operation[(equalsIndex + 1)..].Trim();
            if (formulaIndex >= 0 && valueIndex < 0)
            {
                cell.FormulaA1 = expression.Trim('"');
                continue;
            }

            cell.Value = EvaluateExpression(expression, data);
        }
    }

    private static string EvaluateExpression(string expression, PmReportData data)
    {
        expression = expression.Trim();
        if (expression.StartsWith("If ", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var pieces = Regex.Split(expression, @"\s*&\s*");
        var value = string.Concat(pieces.Select(piece => EvaluatePiece(piece.Trim(), data)));
        return value.Trim('"');
    }

    private static string EvaluatePiece(string piece, PmReportData data)
    {
        if (piece.StartsWith("\"", StringComparison.Ordinal) && piece.EndsWith("\"", StringComparison.Ordinal))
            return piece.Trim('"');

        var field = FieldReference.Match(piece);
        if (!field.Success) return string.Empty;

        var historyIndex = field.Groups["history"].Success
            ? int.Parse(field.Groups["history"].Value, CultureInfo.InvariantCulture) - 1
            : -1;
        var record = historyIndex >= 0 && historyIndex < data.History.Count
            ? data.History[historyIndex]
            : data.Current;

        return record.Fields.TryGetValue(field.Groups["field"].Value, out var value)
            ? value
            : string.Empty;
    }
}
