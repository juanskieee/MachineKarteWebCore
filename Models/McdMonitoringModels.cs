using System;
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
        /// <summary>"TE" or any other user-role string (determines which processes run).</summary>
        public string UserRole { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per process-location cell in the dashboard grid
    // ─────────────────────────────────────────────────────────────────────────

    public class McdCellResult
    {
        /// <summary>Cell key, e.g. "DC_1" or "ATC_65".</summary>
        public string Key { get; set; } = string.Empty;
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<string> Machines { get; set; } = new();
        /// <summary>True when this process is not deployed at this location.</summary>
        public bool NotApplicable { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One process row
    // ─────────────────────────────────────────────────────────────────────────

    public class McdProcessRowResult
    {
        public string Process { get; set; } = string.Empty;
        public int RowTotal { get; set; }
        /// <summary>Key = location name (one of McdMonitoringConfig.AllLocations).</summary>
        public Dictionary<string, McdCellResult> Cells { get; set; } = new();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Full dashboard result
    // ─────────────────────────────────────────────────────────────────────────

    public class McdDashboardResult
    {
        /// <summary>"EEE" or "TE".</summary>
        public string Mode { get; set; } = "EEE";
        public string[] Columns { get; set; } = Array.Empty<string>();
        public List<McdProcessRowResult> Rows { get; set; } = new();
        /// <summary>Column (location) totals; key = location.</summary>
        public Dictionary<string, int> LocationTotals { get; set; } = new();
        /// <summary>Row totals; key = process.</summary>
        public Dictionary<string, int> ProcessTotals { get; set; } = new();
        public int GrandTotal { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Machine list popup
    // ─────────────────────────────────────────────────────────────────────────

    public class McdMachineListResult
    {
        public string Key { get; set; } = string.Empty;
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public List<string> Machines { get; set; } = new();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Static grid configuration
    //
    // Form7's active code path only counts a BM row when:
    //     isMCD = "Yes" AND HaveAction = "No"
    // (NewLoadColor → InputToLabel).  The 7-day / LoadColor path is commented
    // out in Form7.vb and is intentionally not converted.
    //
    // The display columns cover the facility floors, while each process map
    // below follows Form7's own active strLocation loop. In particular, ATC
    // retains PL's two locations and SING retains FL's four locations because
    // the legacy loop does not assign a new array for those processes.
    // ─────────────────────────────────────────────────────────────────────────

    public static class McdMonitoringConfig
    {
        public static readonly string[] AllLocations =
        {
            "LSI1-1F", "LSI1-2F",
            "LSI2-1F", "LSI2-2F", "LSI2-3F",
            "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
        };

        public static readonly string[] EeeProcesses =
        {
            "DC", "DB", "WB", "MP", "PL", "ATC",
            "FL", "SING", "FT", "TP", "EDS", "MEMS"
        };

        public static readonly string[] TeProcesses =
        {
            "FL", "FT", "TP", "MEMS", "EDS"
        };

        public static readonly Dictionary<string, string[]> EeeLocations = new()
        {
            ["DC"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["DB"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["WB"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["MP"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["PL"] = new[] { "LSI1-1F", "LSI2-1F" },
            ["FL"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["FT"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["TP"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["MEMS"] = new[] { "LSI3-2F" },
            ["EDS"] = new[] { "LSI3-4F" },
            ["SING"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            // Form7 does not assign a new location array for ATC after PL.
            ["ATC"] = new[] { "LSI1-1F", "LSI2-1F" },
        };

        public static readonly Dictionary<string, string[]> TeLocations = new()
        {
            ["FL"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
            ["FT"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
            ["TP"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
            ["MEMS"] = new[] { "LSI3-2F" },
            ["EDS"] = new[] { "LSI3-4F" },
        };

        public static string[] GetProcesses(bool isTe) => isTe ? TeProcesses : EeeProcesses;

        public static string[] GetLocations(string process, bool isTe)
        {
            var map = isTe ? TeLocations : EeeLocations;
            return map.TryGetValue(process, out var locs) ? locs : Array.Empty<string>();
        }

        /// <summary>
        /// Build the cell key from a process name and a location string.
        /// ATC + LSI3-1F → "ATC_65" and ATC + LSI3-3F → "ATC_6"
        /// (same special slots as the Form12 mapping).
        /// </summary>
        public static string CellKey(string process, string location)
        {
            if (process == "ATC" && location == "LSI3-1F")
                return "ATC_65";
            if (process == "ATC" && location == "LSI3-3F")
                return "ATC_6";

            if (!EeeLocations.TryGetValue(process, out var locs))
                return $"{process}_?";

            int idx = Array.IndexOf(locs, location) + 1;
            return idx > 0 ? $"{process}_{idx}" : $"{process}_?";
        }

        /// <summary>Reverse lookup used by the machine-list handler.</summary>
        public static (string Process, string Location) ResolveCellKey(string cellKey)
        {
            foreach (var process in EeeProcesses)
            {
                foreach (var location in GetLocations(process, false))
                {
                    if (string.Equals(CellKey(process, location), cellKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return (process, location);
                    }
                }
            }

            return (string.Empty, string.Empty);
        }
    }
}
