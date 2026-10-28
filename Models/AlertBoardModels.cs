using System.Collections.Generic;

namespace MachineKarte.Web.Models
{
    // ─── Raw row returned from SQL ────────────────────────────────────────────
    public class AlertBoardRow
    {
        public string Process { get; set; } = "";
        public string Location { get; set; } = "";
        public string Mcno { get; set; } = "";
        public string Timeofrequest { get; set; } = "";
        public string BMCase { get; set; } = "";
        public int BmId { get; set; }
        public string HaveAction { get; set; } = "No";
        public int BMContentId { get; set; }
        public string? BMContent { get; set; }
        public string? MCStatus { get; set; }
        public string FontColor { get; set; } = "None";
        public string Color { get; set; } = "";   // "Green" when solved
        // TE-mode extra fields
        public string? Priority { get; set; }
    }

    // ─── Cell in the alert grid ───────────────────────────────────────────────
    public class AlertCell
    {
        public int Count { get; set; }
        public List<string> Machines { get; set; } = new();
    }

    // ─── Full board returned to the page ─────────────────────────────────────
    public class AlertBoardData
    {
        // Process → Location-index → AlertCell
        // Location index mirrors the strLocation[] ordering used in VB:
        //   0=LSI1-1F  1=LSI1-2F  2=LSI2-1F  3=LSI2-2F
        //   4=LSI2-3F  5=LSI3-1F  6=LSI3-2F  7=LSI3-3F  8=LSI3-4F

        // Per-process cells (key = "PROCESS_LOCINDEX", e.g. "DC_0", "ATC_5")
        public Dictionary<string, AlertCell> Cells { get; set; } = new();

        // Row totals per location
        public Dictionary<string, int> LocationTotals { get; set; } = new();

        // Grand total
        public int GrandTotal { get; set; }

        // Drilldown machine list (populated on demand)
        public List<string> DrilldownMachines { get; set; } = new();
        public string DrilldownTitle { get; set; } = "";
    }

    // ─── Request model for LoadBoard handler ─────────────────────────────────
    public class AlertBoardRequest
    {
        public string DateFrom { get; set; } = "";
        public string DateTo { get; set; } = "";
        public string User { get; set; } = "";   // "", "CC", "EEE", "VIS", "TE"
    }
}