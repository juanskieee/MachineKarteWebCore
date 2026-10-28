namespace MachineKarte.Web.Models;

// ---------------------------------------------------------------------------
// BmAnalytics – process × location count grid
// Shows machines that are MCD-registered AND already have an action card.
// Mirrors Form12.vb logic (NewLoadColor path only; LoadColor is commented out
// in the source and is intentionally NOT converted).
// ---------------------------------------------------------------------------

public class BmAnalyticsRequest
{
    public string DateFrom { get; set; } = string.Empty;
    public string DateTo { get; set; } = string.Empty;
    public string Mode { get; set; } = "EEE";   // "EEE" or "TE"
}

/// <summary>
/// A single raw BM record returned from the database.
/// </summary>
public class BmAnalyticsRow
{
    public string Devicename { get; set; } = string.Empty;
    public string IsMCD { get; set; } = "No";
    public string Process { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Mcno { get; set; } = string.Empty;
    public string Timeofrequest { get; set; } = string.Empty;
    public string BMCase { get; set; } = string.Empty;
    public int BmId { get; set; }
    public string HaveAction { get; set; } = "No";
    public int BMContentID { get; set; }
    public string BMContent { get; set; } = string.Empty;
    public string MCStatus { get; set; } = string.Empty;
}

/// <summary>
/// One cell in the grid: count of qualifying machines and the machine-number list.
/// </summary>
public class BmAnalyticsCell
{
    public int Count { get; set; }
    public List<string> Mcnos { get; set; } = new();
}

/// <summary>
/// The full grid result returned to the page.
/// Key format: "{PROCESS}_{LOCATION_INDEX}" e.g. "DC_1", "ATC_65".
/// </summary>
public class BmAnalyticsResult
{
    /// <summary>Grid cells keyed by "{process}_{locIndex}".</summary>
    public Dictionary<string, BmAnalyticsCell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Column totals keyed by location label e.g. "LSI1-1F".</summary>
    public Dictionary<string, int> LocationTotals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Row totals keyed by process name e.g. "DC".</summary>
    public Dictionary<string, int> ProcessTotals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Overall grand total.</summary>
    public int GrandTotal { get; set; }
}

// ---------------------------------------------------------------------------
// Static mapping data (mirrors Form12.vb LoadMCName / checkProcessAndLocation)
// ---------------------------------------------------------------------------

public static class BmAnalyticsMappings
{
    // -----------------------------------------------------------------------
    // EEE mode
    // -----------------------------------------------------------------------
    public static readonly string[] EeeProcesses =
        { "DC", "DB", "WB", "MP", "PL", "FL", "FT", "TP", "MEMS", "EDS", "SING", "ATC" };

    // process → ordered location list  (index 1-based, matching the VB label suffix)
    public static readonly Dictionary<string, string[]> EeeLocations = new()
    {
        ["DC"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F"],
        ["DB"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F"],
        ["WB"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F"],
        ["MP"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F"],
        ["PL"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F"],
        ["FL"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["FT"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["TP"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["MEMS"] = ["LSI3-2F"],
        ["EDS"] = ["LSI3-4F"],
        ["SING"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        // ATC has a special slot "65" for LSI3-1F in addition to index 6 (LSI3-3F)
        ["ATC"] = ["LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F", "LSI3-3F"],
    };

    // -----------------------------------------------------------------------
    // TE mode
    // -----------------------------------------------------------------------
    public static readonly string[] TeProcesses =
        { "FL", "FT", "TP", "MEMS", "EDS" };

    public static readonly Dictionary<string, string[]> TeLocations = new()
    {
        ["FL"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["FT"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["TP"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["EDS"] = ["LSI3-4F"],
        ["MEMS"] = ["LSI3-2F"],
    };

    // All unique locations in display order
    public static readonly string[] AllLocations =
    [
        "LSI1-1F", "LSI1-2F",
        "LSI2-1F", "LSI2-2F", "LSI2-3F",
        "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
    ];

    /// <summary>
    /// Build the cell key from a process name and a location string.
    /// ATC + LSI3-1F → "ATC_65"  (special slot from the VB designer).
    /// </summary>
    public static string CellKey(string process, string location)
    {
        if (process == "ATC" && location == "LSI3-1F")
            return "ATC_65";

        var locs = GetLocations(process, "EEE");
        int idx = Array.IndexOf(locs, location) + 1;          // 1-based
        return idx > 0 ? $"{process}_{idx}" : $"{process}_?";
    }

    public static string[] GetProcesses(string mode) =>
        mode == "TE" ? TeProcesses : EeeProcesses;

    public static string[] GetLocations(string process, string mode)
    {
        var map = mode == "TE" ? TeLocations : EeeLocations;
        return map.TryGetValue(process, out var locs) ? locs : [];
    }
}