namespace MachineKarte.Web.Models;

// ---------------------------------------------------------------------------
// BmAnalytics – process × location count grid
// Shows machines that are MCD-registered AND already have an action card.
// The page supports the Form8 and Form12 location profiles; both use the
// active NewLoadColor path while their legacy label layouts differ.
// ---------------------------------------------------------------------------

public class BmAnalyticsRequest
{
    public string DateFrom { get; set; } = string.Empty;
    public string DateTo { get; set; } = string.Empty;
    public string Mode { get; set; } = "EEE";   // "EEE" or "TE"
    public string LegacyForm { get; set; } = "8";
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
/// Key format: "{PROCESS}_{LOCATION_INDEX}" e.g. "DC_1", "ATC_2".
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
// Static mapping data (mirrors Form8.vb LoadMCName / checkProcessAndLocation)
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
        ["DC"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["DB"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["WB"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["MP"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["PL"] = ["LSI1-1F", "LSI2-1F"],
        ["FL"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["FT"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["TP"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["MEMS"] = ["LSI3-2F"],
        ["EDS"] = ["LSI3-4F"],
        ["SING"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        // Form8 retains the PL location array for ATC because no new array is assigned.
        ["ATC"] = ["LSI1-1F", "LSI2-1F"],
    };

    // -----------------------------------------------------------------------
    // TE mode
    // -----------------------------------------------------------------------
    public static readonly string[] TeProcesses =
        { "FL", "FT", "TP", "MEMS", "EDS" };

    public static readonly Dictionary<string, string[]> TeLocations = new()
    {
        ["FL"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["FT"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["TP"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F"],
        ["EDS"] = ["LSI3-4F"],
        ["MEMS"] = ["LSI3-2F"],
    };

    public static readonly Dictionary<string, string[]> Form12EeeLocations = new()
    {
        ["DC"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F"],
        ["DB"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F"],
        ["WB"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F"],
        ["MP"] = ["LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F"],
        ["PL"] = ["LSI1-1F", "LSI2-1F", "LSI3-1F"],
        ["FL"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["FT"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["TP"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["MEMS"] = ["LSI3-2F"],
        ["EDS"] = ["LSI3-4F"],
        ["SING"] = ["LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F"],
        ["ATC"] = ["LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F", "LSI3-3F"],
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
    /// Builds the legacy process/location cell key.
    /// </summary>
    public static string CellKey(string process, string location)
    {
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

    public static string[] GetLocations(string process, string mode, string legacyForm)
    {
        if (legacyForm == "12" &&
            Form12EeeLocations.TryGetValue(process, out var form12Locations))
            return form12Locations;

        return GetLocations(process, mode);
    }

    public static string CellKey(string process, string location, string legacyForm)
    {
        var locs = GetLocations(process, "EEE", legacyForm);
        if (legacyForm == "12" && process == "ATC" && location == "LSI3-1F")
            return "ATC_65";

        var idx = Array.IndexOf(locs, location) + 1;
        return idx > 0 ? $"{process}_{idx}" : $"{process}_?";
    }
}