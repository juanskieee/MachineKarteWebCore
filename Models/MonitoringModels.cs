// Models/MonitoringModels.cs
// Converted from Form1 WinForms — E-Karte System
// C# ASP.NET Core

namespace MachineKarte.Web.Models
{
    /// <summary>
    /// Filter parameters sent from the Monitoring page toolbar.
    /// Maps to: cbUser, cbProcess, cbFloor, cbMachine, dt1, dt2, lblPageNumber
    /// </summary>
    public class MonitoringFilter
    {
        public string UserType { get; set; } = "PM";   // PM | CC | EEE | VIS | TE
        public string Process { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public string Machine { get; set; } = "All";
        public string DateFrom { get; set; } = DateTime.Now.AddDays(-30).ToString("MM/dd/yyyy");
        public string DateTo { get; set; } = DateTime.Now.AddDays(1).ToString("MM/dd/yyyy");
        public int Page { get; set; } = 1;
    }

    /// <summary>
    /// One row returned by the paged machine query (strQuery2 in LoadMCName).
    /// Used to drive MachineMonitoringCard columns.
    /// </summary>
    public class MachineSummary
    {
        public string MachineNo { get; set; } = string.Empty;   // MCNO / testerno
        public string MachineType { get; set; } = string.Empty;   // MCType
        public int BMCount { get; set; }                   // count(mcno) / count(MCType)  = MACHINENAME
        public int ActionLine { get; set; } = 4;              // COALESCE(c.actionline,4)
    }

    /// <summary>
    /// One BM card row inside a machine column (strQuery in LoadMCName1 / uC_Test.LoadMain).
    /// </summary>
    public class BMCardRow
    {
        public string DeviceName { get; set; } = string.Empty;
        public string IsMCD { get; set; } = "No";
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string MachineNo { get; set; } = string.Empty;
        public string MachineType { get; set; } = string.Empty;
        public string TimeOfRequest { get; set; } = string.Empty;
        public string BMCase { get; set; } = string.Empty;
        public string BMCaseId { get; set; } = string.Empty;
        public int BMId { get; set; }
        public int OcapCount { get; set; }
        public string HaveAction { get; set; } = "No";
        public string BMContentId { get; set; } = string.Empty;
        public string BMContent { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string MCStatus { get; set; } = string.Empty;
        public string FontColor { get; set; } = "None";
    }

    /// <summary>
    /// Summary counts shown in Panel5 / the legend/counter panel (BM totals per floor).
    /// Maps to Label_1..Label_9, lbl_1..lbl_9, Label1_1..Label1_9 groups.
    /// </summary>
    public class FloorSummary
    {
        // Index 1-9 maps to: LSI1-1F, LSI1-2F, LSI2-1F, LSI2-2F, LSI2-3F,
        //                    LSI3-2F, LSI3-4F, LSI3-1F, LSI3-3F
        public string FloorCode { get; set; } = string.Empty;
        public int BMCount { get; set; }
        public int MCDCount { get; set; }
        public int MachineCount { get; set; }
    }

    /// <summary>
    /// Response envelope for AJAX calls.
    /// </summary>
    public class AjaxResult<T>
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    /// <summary>
    /// Result of GetMachines dropdown AJAX call (floor → machine list).
    /// </summary>
    public class MachineListResult
    {
        public List<string> Machines { get; set; } = new();
    }

    /// <summary>
    /// Full dashboard payload returned by Search AJAX call.
    /// </summary>
    public class MonitoringDashboardResult
    {
        public List<MachineSummary> MachineSummaries { get; set; } = new();
        public List<FloorSummary> FloorSummaries { get; set; } = new();
        public int TotalPages { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
    }

    /// <summary>
    /// BM cards for one specific machine column — loaded per card via AJAX.
    /// </summary>
    public class MachineCardResult
    {
        public string MachineNo { get; set; } = string.Empty;
        public string MachineType { get; set; } = string.Empty;
        public int ActionLine { get; set; } = 4;
        public List<BMCardRow> BMCards { get; set; } = new();
    }
}