using System.Collections.Generic;

namespace MachineKarte.Web.Models
{
    // ── Query parameters ─────────────────────────────────────────────────────────
    public class MachineSummaryQuery
    {
        public string DateFrom { get; set; } = string.Empty;
        public string DateTo { get; set; } = string.Empty;
        /// <summary>"EEE" (default) or "TE"</summary>
        public string UserMode { get; set; } = "EEE";
    }

    // ── One grid cell ─────────────────────────────────────────────────────────────
    public class SummaryCellData
    {
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<string> Machines { get; set; } = new();
        /// <summary>True when this process is not deployed at this location.</summary>
        public bool NotApplicable { get; set; }
    }

    // ── One process row ───────────────────────────────────────────────────────────
    public class SummaryProcessRow
    {
        public string Process { get; set; } = string.Empty;
        public int RowTotal { get; set; }
        /// <summary>Key = location name (one of MachineSummaryConfig.DisplayColumns).</summary>
        public Dictionary<string, SummaryCellData> Cells { get; set; } = new();
    }

    // ── Full grid result ──────────────────────────────────────────────────────────
    public class MachineSummaryResult
    {
        /// <summary>"EEE" or "TE".</summary>
        public string Mode { get; set; } = "EEE";
        public List<SummaryProcessRow> Rows { get; set; } = new();
        /// <summary>Column (location) totals; key = location.</summary>
        public Dictionary<string, int> LocationTotals { get; set; } = new();
        /// <summary>Row totals; key = process.</summary>
        public Dictionary<string, int> ProcessTotals { get; set; } = new();
        public int GrandTotal { get; set; }
        public string[] Columns { get; set; } = System.Array.Empty<string>();
    }

    // ── Machine list result for a clicked cell ────────────────────────────────────
    public class MachineSummaryMachineListResult
    {
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public List<string> Machines { get; set; } = new();
    }

    // ── Static configuration (process → valid locations, column order) ────────────
    public static class MachineSummaryConfig
    {
        /// <summary>
        /// Column display order (left-to-right), derived from the Designer label x-positions
        /// in Panel2.  These are ALL distinct locations across both modes.
        /// </summary>
        public static readonly string[] DisplayColumns =
        {
            "LSI1-1F", "LSI1-2F",
            "LSI2-1F", "LSI2-2F", "LSI2-3F",
            "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
        };

        /// <summary>
        /// Row display order for EEE mode (matches Panel2 label y-positions).
        /// </summary>
        public static readonly string[] EeeProcessOrder =
        {
            "DC", "DB", "WB", "MP", "PL", "ATC",
            "FL", "SING", "FT", "TP", "EDS", "MEMS"
        };

        /// <summary>
        /// Row display order for TE mode.
        /// </summary>
        public static readonly string[] TeProcessOrder =
        {
            "FL", "FT", "TP", "MEMS", "EDS"
        };

        /// <summary>
        /// Process → locations queried in EEE mode.
        /// Mirrors the completed Form11 label layout and the MCD monitoring grid.
        /// </summary>
        public static readonly Dictionary<string, string[]> ProcessLocations =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["DC"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F" },
                ["DB"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F" },
                ["WB"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F" },
                ["MP"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F" },
                ["PL"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F" },
                ["ATC"] = new[] { "LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F", "LSI3-3F" },
                ["FL"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["SING"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["FT"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["TP"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["EDS"] = new[] { "LSI3-4F" },
                ["MEMS"] = new[] { "LSI3-2F" },
            };

        /// <summary>
        /// Process → locations queried in TE mode.
        /// </summary>
        public static readonly Dictionary<string, string[]> ProcessLocations_TE =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["FL"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["FT"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["TP"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
                ["MEMS"] = new[] { "LSI3-2F" },
                ["EDS"] = new[] { "LSI3-4F" },
            };

        public static string[] GetProcesses(bool isTe)
            => isTe ? TeProcessOrder : EeeProcessOrder;

        public static string[] GetLocations(string process, bool isTe)
        {
            var map = isTe ? ProcessLocations_TE : ProcessLocations;
            return map.TryGetValue(process, out var locations)
                ? locations
                : System.Array.Empty<string>();
        }

        public static bool IsValidProcess(string process, bool isTe)
            => GetProcesses(isTe).Contains(process, System.StringComparer.OrdinalIgnoreCase);
    }
}