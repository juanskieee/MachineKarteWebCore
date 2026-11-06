namespace MachineKarte.Web.Models;

public sealed record BmAnalyticsViewModel(string LegacyForm);

public sealed record ReportWorkflowViewModel(int BmId, string Process);

public sealed record ReportOutputViewModel(
    string Workflow, string StatusId, string DownloadId, string DocumentKind, string PreviewTitle);

public sealed record OcapViewerViewModel(int BmId, string BmProcess, string BmCaseId);

public sealed record RawDataViewModel(
    string Process, string Location, string Machine, string DateFrom, string DateTo);
