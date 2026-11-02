using System.Collections.Generic;

namespace MachineKarte.Web.Models
{
    public static class AlertBoardConfig
    {
        public static readonly string[] ProcessOrder =
            ["DC", "DB", "WB", "MP", "PL", "ATC", "FL", "SING", "FT", "TP", "EDS", "MEMS"];

        public static readonly string[] DisplayColumns = MachineSummaryConfig.DisplayColumns;

        // Keys retain the existing API. Query locations and designer columns are distinct
        // in Form2: MP's LSI3-2F query feeds the white label under LSI3-1F.
        public static readonly Dictionary<string, (string Process, string Location, string Column)> Cells = new()
        {
            ["DC_0"] = ("DC", "LSI1-1F", "LSI1-1F"),
            ["DC_2"] = ("DC", "LSI2-1F", "LSI2-1F"),
            ["DC_3"] = ("DC", "LSI2-2F", "LSI2-2F"),
            ["DC_4"] = ("DC", "LSI2-3F", "LSI2-3F"),
            ["DC_6"] = ("DC", "LSI3-2F", "LSI3-2F"),
            ["DB_0"] = ("DB", "LSI1-1F", "LSI1-1F"),
            ["DB_2"] = ("DB", "LSI2-1F", "LSI2-1F"),
            ["DB_3"] = ("DB", "LSI2-2F", "LSI2-2F"),
            ["DB_4"] = ("DB", "LSI2-3F", "LSI2-3F"),
            ["DB_6"] = ("DB", "LSI3-2F", "LSI3-2F"),
            ["WB_0"] = ("WB", "LSI1-1F", "LSI1-1F"),
            ["WB_2"] = ("WB", "LSI2-1F", "LSI2-1F"),
            ["WB_3"] = ("WB", "LSI2-2F", "LSI2-2F"),
            ["WB_4"] = ("WB", "LSI2-3F", "LSI2-3F"),
            ["WB_6"] = ("WB", "LSI3-2F", "LSI3-2F"),
            ["MP_0"] = ("MP", "LSI1-1F", "LSI1-1F"),
            ["MP_2"] = ("MP", "LSI2-1F", "LSI2-1F"),
            ["MP_3"] = ("MP", "LSI2-2F", "LSI2-2F"),
            ["MP_4"] = ("MP", "LSI2-3F", "LSI2-3F"),
            ["MP_6"] = ("MP", "LSI3-2F", "LSI3-1F"),
            ["PL_0"] = ("PL", "LSI1-1F", "LSI1-1F"),
            ["PL_2"] = ("PL", "LSI2-1F", "LSI2-1F"),
            ["PL_3"] = ("PL", "LSI2-2F", "LSI2-2F"),
            ["PL_4"] = ("PL", "LSI2-3F", "LSI2-3F"),
            ["PL_5"] = ("PL", "LSI3-1F", "LSI3-1F"),
            ["ATC_0"] = ("ATC", "LSI1-1F", "LSI1-1F"),
            ["ATC_1"] = ("ATC", "LSI1-2F", "LSI1-2F"),
            ["ATC_2"] = ("ATC", "LSI2-1F", "LSI2-1F"),
            ["ATC_3"] = ("ATC", "LSI2-2F", "LSI2-2F"),
            ["ATC_4"] = ("ATC", "LSI2-3F", "LSI2-3F"),
            ["ATC_65"] = ("ATC", "LSI3-1F", "LSI3-1F"),
            ["ATC_6"] = ("ATC", "LSI3-3F", "LSI3-3F"),
            ["FL_1"] = ("FL", "LSI1-2F", "LSI1-2F"),
            ["FL_2"] = ("FL", "LSI2-1F", "LSI2-1F"),
            ["FL_3"] = ("FL", "LSI2-2F", "LSI2-2F"),
            ["FL_4"] = ("FL", "LSI2-3F", "LSI2-3F"),
            ["FL_5"] = ("FL", "LSI3-3F", "LSI3-3F"),
            ["SING_1"] = ("SING", "LSI1-2F", "LSI1-2F"),
            ["SING_2"] = ("SING", "LSI2-1F", "LSI2-1F"),
            ["SING_3"] = ("SING", "LSI2-2F", "LSI2-2F"),
            ["SING_4"] = ("SING", "LSI2-3F", "LSI2-3F"),
            ["SING_5"] = ("SING", "LSI3-3F", "LSI3-3F"),
            ["FT_1"] = ("FT", "LSI1-2F", "LSI1-2F"),
            ["FT_2"] = ("FT", "LSI2-1F", "LSI2-1F"),
            ["FT_3"] = ("FT", "LSI2-2F", "LSI2-2F"),
            ["FT_4"] = ("FT", "LSI2-3F", "LSI2-3F"),
            ["FT_5"] = ("FT", "LSI3-3F", "LSI3-3F"),
            ["TP_1"] = ("TP", "LSI1-2F", "LSI1-2F"),
            ["TP_2"] = ("TP", "LSI2-1F", "LSI2-1F"),
            ["TP_3"] = ("TP", "LSI2-2F", "LSI2-2F"),
            ["TP_4"] = ("TP", "LSI2-3F", "LSI2-3F"),
            ["TP_5"] = ("TP", "LSI3-3F", "LSI3-3F"),
            ["EDS_1"] = ("EDS", "LSI3-4F", "LSI3-4F"),
            ["MEMS_1"] = ("MEMS", "LSI3-2F", "LSI3-2F")
        };

        public static readonly Dictionary<(string Process, string Location), string> CellKeys =
            Cells.ToDictionary(cell => (cell.Value.Process, cell.Value.Location), cell => cell.Key);
    }

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
        // Existing API keys are resolved by AlertBoardConfig, independently of
        // query-loop indices and designer columns (e.g. FL_5 and EDS_1).
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
