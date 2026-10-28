using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services;

/// <summary>
/// Orchestrates the BM Analytics grid build.
///
/// Active logic: NewLoadColor() only.
///   Count cell += 1 when isMCD = "Yes" AND HaveAction = "Yes"
///   (i.e. machines with MCD that already have an action card).
///
/// LoadColor / 7-day recurrence path is commented out in the source
/// and is intentionally NOT converted here.
/// </summary>
public class BmAnalyticsService
{
    private readonly BmAnalyticsRepository _repo;

    public BmAnalyticsService(BmAnalyticsRepository repo) => _repo = repo;

    public async Task<BmAnalyticsResult> BuildGridAsync(BmAnalyticsRequest req)
    {
        if (!DateTime.TryParse(req.DateFrom, out var dtFrom)) dtFrom = DateTime.Today.AddDays(-7);
        if (!DateTime.TryParse(req.DateTo, out var dtTo)) dtTo = DateTime.Today;

        string mode = req.Mode?.ToUpperInvariant() == "TE" ? "TE" : "EEE";
        string[] procs = BmAnalyticsMappings.GetProcesses(mode);

        var result = new BmAnalyticsResult();

        // ---------------------------------------------------------------
        // Iterate every process → location combination (mirrors LoadMCName)
        // ---------------------------------------------------------------
        foreach (string process in procs)
        {
            string[] locations = BmAnalyticsMappings.GetLocations(process, mode);

            foreach (string location in locations)
            {
                List<BmAnalyticsRow> rows =
                    await _repo.GetRowsAsync(process, location, mode, dtFrom, dtTo);

                if (rows.Count == 0) continue;

                // NewLoadColor logic: isMCD=Yes AND HaveAction=Yes → count it
                ApplyNewLoadColor(rows, result);
            }
        }

        // ---------------------------------------------------------------
        // Build location totals
        // ---------------------------------------------------------------
        foreach (string loc in BmAnalyticsMappings.AllLocations)
        {
            int total = 0;
            foreach (var kv in result.Cells)
            {
                // Resolve which location this cell key maps to
                if (CellKeyMatchesLocation(kv.Key, loc, mode))
                    total += kv.Value.Count;
            }
            result.LocationTotals[loc] = total;
        }

        // ---------------------------------------------------------------
        // Build process totals (sum across all location slots for a process)
        // ---------------------------------------------------------------
        foreach (string proc in procs)
        {
            int total = result.Cells
                .Where(kv => kv.Key.StartsWith(proc + "_", StringComparison.OrdinalIgnoreCase))
                .Sum(kv => kv.Value.Count);
            result.ProcessTotals[proc] = total;
        }

        result.GrandTotal = result.ProcessTotals.Values.Sum();

        return result;
    }

    // ------------------------------------------------------------------
    // NewLoadColor: mirrors Form12.vb NewLoadColor + InputToLabel
    // Only count rows where isMCD=Yes AND HaveAction=Yes
    // ------------------------------------------------------------------
    private static void ApplyNewLoadColor(List<BmAnalyticsRow> rows, BmAnalyticsResult result)
    {
        // Track mcnos already added to avoid duplicates (mirrors RemoveDuplicates())
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.IsMCD != "Yes" || row.HaveAction != "Yes") continue;

            string key = BmAnalyticsMappings.CellKey(row.Process, row.Location);

            if (!result.Cells.TryGetValue(key, out var cell))
            {
                cell = new BmAnalyticsCell();
                result.Cells[key] = cell;
            }

            // Deduplicate by process+location+mcno (mirrors RemoveDuplicates in ListBox)
            string dedupeKey = $"{key}|{row.Mcno}";
            if (!seen.Add(dedupeKey)) continue;

            cell.Count++;
            cell.Mcnos.Add(row.Mcno);
        }
    }

    // ------------------------------------------------------------------
    // Reverse-map a cell key like "DC_1" or "ATC_65" to a location string
    // ------------------------------------------------------------------
    private static bool CellKeyMatchesLocation(string cellKey, string location, string mode)
    {
        // cellKey format: "{PROCESS}_{INDEX}"  or "ATC_65"
        int sep = cellKey.LastIndexOf('_');
        if (sep < 0) return false;

        string proc = cellKey[..sep];
        string idxStr = cellKey[(sep + 1)..];

        if (proc == "ATC" && idxStr == "65")
            return location == "LSI3-1F";

        if (!int.TryParse(idxStr, out int idx)) return false;

        string[] locs = BmAnalyticsMappings.GetLocations(proc, mode);
        if (idx < 1 || idx > locs.Length) return false;
        return locs[idx - 1] == location;
    }
}