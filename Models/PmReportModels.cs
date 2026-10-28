using System.Collections.Generic;

namespace MachineKarte.Web.Models;

public sealed class PmReportRequest
{
    public string Process { get; set; } = string.Empty;
    public int BmId { get; set; }
    public string TimeOfRequest { get; set; } = string.Empty;
    public string MachineNo { get; set; } = string.Empty;
}

public sealed class PmReportRecord
{
    public Dictionary<string, string> Fields { get; } = new(System.StringComparer.OrdinalIgnoreCase);
}

public sealed class PmReportData
{
    public string Process { get; init; } = string.Empty;
    public int BmId { get; init; }
    public PmReportRecord Current { get; init; } = new();
    public List<PmReportRecord> History { get; init; } = new();
}

public sealed class PmReportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;

    public static PmReportResult Failed(string message) => new()
    {
        Success = false,
        Message = message
    };
}

public sealed record PmReportTemplateDefinition(
    string Process,
    string TemplateFile,
    string WorksheetName,
    string ViewName,
    bool HasHistory);
