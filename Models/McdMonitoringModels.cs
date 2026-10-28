using System.Collections.Generic;

namespace MachineKarte.Web.Models
{
    // ─────────────────────────────────────────────────────────────────────────
    // Request / filter
    // ─────────────────────────────────────────────────────────────────────────

    public class McdMonitoringFilter
    {
        public string DateFrom { get; set; } = string.Empty;
        public string DateTo { get; set; } = string.Empty;
        /// <summary>"TE" or any other user-role string (determines which processes/queries run).</summary>
        public string UserRole { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-row data returned from the DB
    // ─────────────────────────────────────────────────────────────────────────

    public class McdBmRow
    {
        public string DeviceName { get; set; } = string.Empty;
        public string IsMcd { get; set; } = "No";
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Mcno { get; set; } = string.Empty;
        public string TimeOfRequest { get; set; } = string.Empty;
        public string BmCase { get; set; } = string.Empty;
        public int BmId { get; set; }
        public string HaveAction { get; set; } = "No";
        public int BmContentId { get; set; }
        public string BmContent { get; set; } = string.Empty;
        public string McStatus { get; set; } = string.Empty;

        // Derived fields (set during processing, not from DB)
        public string FontColor { get; set; } = "None";
        public string Color { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per process-location cell in the dashboard grid
    // ─────────────────────────────────────────────────────────────────────────

    public class McdCellCount
    {
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int Count { get; set; }
        /// <summary>Comma-separated list of machine numbers that make up this count.</summary>
        public List<string> Machines { get; set; } = new();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Full dashboard result
    // ─────────────────────────────────────────────────────────────────────────

    public class McdDashboardResult
    {
        public List<McdCellCount> Cells { get; set; } = new();

        // Floor / building totals  (mirrors TotalLabel logic in VB)
        public int TotalLsi1_1F { get; set; }   // LSI1-1F  – DC+DB+WB+MP+PL
        public int TotalLsi1_2F { get; set; }   // LSI1-2F  – FL+FT+TP
        public int TotalLsi2_1F { get; set; }   // LSI2-1F  – all processes
        public int TotalLsi2_2F { get; set; }   // LSI2-2F
        public int TotalLsi2_3F { get; set; }   // LSI2-3F
        public int TotalLsi3 { get; set; }   // LSI3 combined (MEMS+FT5+TP5+MP5)
        public int TotalLsi3_4F { get; set; }   // EDS only
        public int GrandTotal { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Machine list popup
    // ─────────────────────────────────────────────────────────────────────────

    public class McdMachineListResult
    {
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public List<string> Machines { get; set; } = new();
    }
}