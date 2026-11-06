using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public class AlertBoardRepository
{
    private readonly string _connectionString;

    // Domain maps
    private static readonly string[] ProcessesNormal =
        { "DC", "DB", "WB", "MP", "PL", "FL", "FT", "TP", "MEMS", "EDS", "ATC", "SING" };

    private static readonly string[] ProcessesTE =
        { "FL", "FT", "TP", "MEMS", "EDS" };

    private static readonly string[] Locations =
    {
        // Keep Form2's repeated LSI3-1F read for count parity.
        "LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F",
        "LSI2-3F", "LSI3-1F", "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
    };

    public AlertBoardRepository(IConfiguration configuration) =>
        _connectionString = RepositorySql.GetConnectionString(configuration);

    // Public API
    /// <summary>
    /// Fetches all BM rows for the requested date range and user mode,
    /// applies the duplicate-case / 7-day / no-action-card filtering,
    /// and returns the aggregated board data.
    /// </summary>
    public async Task<AlertBoardData> LoadBoardAsync(AlertBoardRequest req, CancellationToken cancellationToken = default)
    {
        var board = new AlertBoardData();

        string[] processes = req.User == "TE" ? ProcessesTE : ProcessesNormal;

        // Build per-user SQL fragments (mirrors VB AddQuery / ActionQuery)
        string addQuery = BuildAddQuery(req.User);
        string actionTable = BuildActionTable(req.User);

        var queries = processes.Select(process => req.User == "TE"
            ? BuildQueryTE(process)
            : BuildQueryNormal(process, addQuery, actionTable));

        await foreach (var (_, rows) in MonitoringQueryBatch.ReadAsync(queries,
            sql => FetchRowsAsync(sql, req.DateFrom, req.DateTo, "All", cancellationToken), cancellationToken))
        {
            // One process read replaces ten floor reads. Replay the original floor order,
            // including LSI3-1F twice, with fresh color state for each legacy pass.
            var byFloor = rows.ToLookup(row => row.Location.TrimEnd(' '), StringComparer.OrdinalIgnoreCase);
            foreach (var location in Locations)
            {
                var floorRows = byFloor[location].ToList();
                if (floorRows.Count == 0) continue;
                foreach (var row in floorRows)
                {
                    row.FontColor = "None";
                    row.Color = "";
                }
                var alertRows = ApplySolveLogic(floorRows);
                if (req.User == "TE") ApplyColorTE(alertRows, board);
                else ApplyColorNormal(alertRows, board);
            }
        }

        BuildTotals(board);
        return board;
    }

    /// <summary>Returns machine list for a specific cell (PROCESS_LOCINDEX).</summary>
    public List<string> GetCellMachines(AlertBoardData board, string cellKey)
        => board.Cells.TryGetValue(cellKey, out var cell) ? cell.Machines : new();

    // SQL builders
    private static string BuildAddQuery(string user) => user switch
    {
        "CC" => "(Turnover IN ('CC')  AND BMStatusID IN (6,7,8,9,11,20,19,14))",
        "EEE" => "(Turnover IN ('PE')  AND BMStatusID IN (6,7,8,9,11,20,19,14))",
        "VIS" => "(Turnover IN ('VIS') AND BMStatusID IN (6,7,8,9,11,20,19,14))",
        _ => "(a.BMStatusID not in ('30','31'))"
    };

    private static string BuildActionTable(string user) => user switch
    {
        "CC" => "MachineKarte_ActionCard_CC",
        "EEE" => "MachineKarte_ActionCard_EEE",
        "VIS" => "MachineKarte_ActionCard_VIS",
        _ => "MachineKarte_ActionCard"
    };

    private static string BuildQueryNormal(string process, string addQuery, string actionTable)
    {
        return $@"
SELECT a.process, a.location, a.Mcno AS Mcno,
       CONVERT(char(10),a.Timeofrequest,101) + ' ' + CONVERT(char(8),a.Timeofrequest,108) AS Timeofrequest,
   b.BMCase AS BMCase, a.bmid,
   CASE WHEN EXISTS (SELECT 1 FROM {actionTable}
           WHERE bmid = a.bmID AND machineno = a.mcno) THEN 'Yes' ELSE 'No' END AS HaveAction,
   COALESCE(CASE
       WHEN a.AcredBMContentID <> a.BMContentID THEN
           CASE WHEN a.AcredBMContentID = ''   THEN a.BMContentID
                WHEN a.AcredBMContentID = '0'  THEN a.BMContentID
                WHEN a.AcredBMContentID IS NULL THEN a.BMContentID
                ELSE a.AcredBMContentID END
       ELSE a.BMContentID END, 0) AS BMContentID,
   (SELECT BMContent FROM BMContent
    WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
   c.mcstatus AS MCStatus,
   'None' AS FontColor
FROM   [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b ON a.BMCaseID = b.BMCaseID
OUTER APPLY (SELECT TOP (1) ac.mcstatus FROM [eMaintenanceSystemDB].[dbo].[{actionTable}] ac
         WHERE ac.bmID = a.bmID AND ac.machineno = a.mcno ORDER BY ac.Seqno DESC) c
WHERE  (a.timeofrequest BETWEEN @Range1 AND @Range2)
  AND  (a.Pmcategoryid = '1')
  AND  {addQuery}
  AND  (a.bmcaseid <> 0)
  AND  (@Location = 'All' OR a.Location = @Location)
ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.BMCase";
    }

    private static string BuildQueryTE(string process)
    {
        return $@"
SELECT a.process, a.location, a.testerNo AS Mcno,
       CONVERT(char(10),a.Timeofrequest,101) + ' ' + CONVERT(char(8),a.Timeofrequest,108) AS Timeofrequest,
   b.bmteunit AS BMCase, a.bmid,
   CASE WHEN EXISTS (SELECT 1 FROM MachineKarte_ActionCard_TE WHERE bmid = a.bmID) THEN 'Yes' ELSE 'No' END AS HaveAction,
   COALESCE(CASE
       WHEN a.AcredBMContentID <> a.BMContentID THEN
           CASE WHEN a.AcredBMContentID = ''   THEN a.BMContentID
                WHEN a.AcredBMContentID = '0'  THEN a.BMContentID
                WHEN a.AcredBMContentID IS NULL THEN a.BMContentID
                ELSE a.AcredBMContentID END
       ELSE a.BMContentID END, 0) AS BMContentID,
   NULL AS BMContent,
   c.mcstatus AS MCStatus,
   'None' AS FontColor,
   a.Priority
FROM   [eMaintenanceSystemDB].[dbo].[TEBMMaintenance] a
INNER JOIN [eMaintenanceSystemDB].[dbo].[BMTEUnit] b ON a.bmunitid = b.bmteunitid
OUTER APPLY (SELECT TOP (1) ac.mcstatus FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] ac
         WHERE ac.bmID = a.bmID ORDER BY ac.Seqno DESC) c
WHERE  (a.timeofrequest BETWEEN @Range1 AND @Range2)
  AND  a.process = '{process}'
  AND  (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
  AND  (a.BMStatusID NOT IN ('30','31','1','2','3','7','17'))
  AND  (a.bmunitid <> 0)
  AND  (@Location = 'All' OR a.Location = @Location)
ORDER BY a.testerNo DESC, a.MCType DESC, a.Timeofrequest DESC, b.bmteunit";
    }

    // Data fetch
    private async Task<List<AlertBoardRow>> FetchRowsAsync(
        string sql, string dateFrom, string dateTo, string location, CancellationToken cancellationToken)
    {
        var list = new List<AlertBoardRow>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        RepositorySql.AddDateRange(cmd, dateFrom, dateTo);
        RepositorySql.AddVarChar(cmd, "@Location", location, 20);

        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        bool hasPriority = RepositorySql.GetColumns(rdr).Contains("Priority");
        while (await rdr.ReadAsync(cancellationToken))
        {
            var row = new AlertBoardRow
            {
                Process = rdr["process"].ToString()!,
                Location = rdr["location"].ToString()!,
                Mcno = rdr["Mcno"].ToString()!,
                Timeofrequest = rdr["Timeofrequest"].ToString()!,
                BMCase = rdr["BMCase"].ToString()!,
                BmId = Convert.ToInt32(rdr["bmid"]),
                HaveAction = rdr["HaveAction"].ToString()!,
                BMContentId = Convert.ToInt32(rdr["BMContentID"]),
                BMContent = rdr["BMContent"] as string,
                MCStatus = rdr["MCStatus"] as string,
                FontColor = "None",
                Color = ""
            };
            row.Priority = hasPriority ? rdr["Priority"] as string : null;
            list.Add(row);
        }
        return list;
    }

    // Solve / color logic (mirrors VB CheckIfSolve + GetDuplicateRowsByColumn)
    /// <summary>
    /// Port of VB CheckIfSolve → GetDuplicateRowsByColumn → AreOtherRowsSame.
    /// Sorts by Mcno DESC, BMCase ASC, Timeofrequest DESC then marks duplicate-case
    /// rows that have HaveAction=Yes and span ≥7 days as Color=Green.
    /// Merges the non-duplicate middle rows back before the repeat/color pass.
    /// </summary>
    private static List<AlertBoardRow> ApplySolveLogic(List<AlertBoardRow> rows)
    {
        if (rows.Count == 0) return rows;
        // CheckIfSolve captures the query's first timestamp before sorting by case.
        string firstColumnDate = rows[0].Timeofrequest;

        // DataView sorts the formatted timestamp as text; preserve ties in input order.
        rows = rows.OrderByDescending(row => row.Mcno, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.BMCase, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(row => row.Timeofrequest, StringComparer.CurrentCultureIgnoreCase).ToList();

        // Separate duplicate-case rows from non-duplicate-case rows
        var duplicates = new List<AlertBoardRow>();
        var nonDuplicates = new List<AlertBoardRow>();

        int rowCount = rows.Count;
        for (int i = 0; i < rowCount - 1; i++)
        {
            var cur = rows[i];
            var next = rows[i + 1];

            if (cur.BMCase == next.BMCase)
            {
                duplicates.Add(cur);
            }
            else
            {
                if (i != 0)
                {
                    var prev = rows[i - 1];
                    if (cur.BMCase == prev.BMCase)
                        duplicates.Add(cur);
                    else
                        nonDuplicates.Add(cur);
                }
            }

            // Last row
            if (i == rowCount - 2)
            {
                var last = rows[rowCount - 1];
                if (cur.BMCase == last.BMCase)
                    duplicates.Add(last);
            }
        }

        // AreOtherRowsSame — mark solved rows (Color=Green)
        MarkSolvedRows(duplicates, firstColumnDate);
        // Form2 merges the non-duplicate middle rows back before LoadColor.
        duplicates.AddRange(nonDuplicates);
        return duplicates;
    }

    private static void MarkSolvedRows(List<AlertBoardRow> table, string firstColumnDate)
    {
        // Preserve the first row of each adjacent case run, including machine boundaries.
        var uniqueFirst = new List<AlertBoardRow>();
        for (int i = 0; i < table.Count - 1; i++)
        {
            if (i == 0) uniqueFirst.Add(table[i]);
            if (table[i].BMCase != table[i + 1].BMCase) uniqueFirst.Add(table[i + 1]);
        }

        var dateNow = DateTime.ParseExact(firstColumnDate, "MM/dd/yyyy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);

        foreach (var row in table)
        {
            foreach (var unique in uniqueFirst)
            {
                if (unique.BMCase != row.BMCase) continue;

                var dateY = DateTime.ParseExact(unique.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                var daysFromNow = (dateNow - dateY).Days;

                if (daysFromNow >= 7)
                {
                    if (row.HaveAction == "Yes") row.Color = "Green";
                    break;
                }

                if (unique.Timeofrequest != row.Timeofrequest)
                {
                    var dateRow = DateTime.ParseExact(row.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture);
                    if ((dateY - dateRow).Days >= 7)
                    {
                        if (row.HaveAction == "Yes") row.Color = "Green";
                    }
                }
            }
        }
    }

    // Font color / 7-day cascade (mirrors VB LoadColor / LoadColor_TE)
    private static void ApplyColorNormal(List<AlertBoardRow> rows, AlertBoardData board)
    {

        rows = SortForColor(rows);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.FontColor != "None") continue;

            string fontColor = GetFontColor(row.BMContentId, row.Process);
            int remaining = rows.Count - 1 - i;
            int depth = Math.Min(remaining, 5);

            bool triggered = false;
            for (int matchCount = depth; matchCount >= 1 && !triggered; matchCount--)
            {
                if (!AreNextNEqual(rows, i, matchCount, r => r.BMContentId.ToString(), r => r.Mcno)) continue;
                int caseMatch = 0;
                for (int c = matchCount; c >= 1; c--)
                {
                    if (AreNextNEqual(rows, i, c, r => r.BMCase, r => r.Mcno))
                    { caseMatch = c; break; }
                }
                if (caseMatch == 0) break;
                CheckAndMarkSevenDays(rows, i, caseMatch, fontColor);
                var r = rows[i];
                if (r.FontColor != "Black" && r.FontColor != "None"
                    && r.HaveAction != "Yes" && r.Color != "Green")
                {
                    AddToBoard(board, r.Process, r.Location, r.Mcno);
                }
                triggered = true; // The VB If/ElseIf selects one matching content branch.
            }
        }
    }

    private static void ApplyColorTE(List<AlertBoardRow> rows, AlertBoardData board)
    {
        rows = SortForColor(rows);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.FontColor != "None") continue;
            if (row.Priority != "QUALITY BM") continue;

            const string fontColor = "Red";
            int remaining = rows.Count - 1 - i;
            int depth = Math.Min(remaining, 3);   // Form2 TE checks at most three subsequent rows.

            bool triggered = false;
            for (int matchCount = depth; matchCount >= 1 && !triggered; matchCount--)
            {
                if (!AreNextNEqual(rows, i, matchCount, r => r.Priority ?? "", r => r.Mcno)) continue;
                int caseMatch = 0;
                for (int c = matchCount; c >= 1; c--)
                {
                    if (AreNextNEqual(rows, i, c, r => r.BMCase, r => r.Mcno))
                    { caseMatch = c; break; }
                }
                if (caseMatch == 0) break;
                CheckAndMarkSevenDays(rows, i, caseMatch, fontColor);
                var r = rows[i];
                if (r.FontColor != "Black" && r.FontColor != "None"
                    && r.HaveAction != "Yes" && r.Color != "Green")
                {
                    AddToBoard(board, r.Process, r.Location, r.Mcno);
                }
                triggered = true;
            }
        }
    }

    // Helpers
    private static List<AlertBoardRow> SortForColor(List<AlertBoardRow> rows)
        => rows.OrderByDescending(row => row.Mcno, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(row => row.Timeofrequest, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.BMCase, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static bool AreNextNEqual(
        List<AlertBoardRow> rows, int index, int n,
        Func<AlertBoardRow, string> valueSelector,
        Func<AlertBoardRow, string> mcnoSelector)
    {
        string baseValue = valueSelector(rows[index]);
        string baseMcno = mcnoSelector(rows[index]);
        for (int k = index + 1; k <= index + n && k < rows.Count; k++)
        {
            if (mcnoSelector(rows[k]) != baseMcno) return false;
            if (valueSelector(rows[k]) != baseValue) return false;
        }
        return true;
    }

    private static void CheckAndMarkSevenDays(
        List<AlertBoardRow> rows, int index, int count, string fontColor)
    {
        var baseDate = ParseDate(rows[index].Timeofrequest);
        if (count == 1)
        {
            var nextDate = ParseDate(rows[index + 1].Timeofrequest);
            var span = (baseDate - nextDate).Days;
            if (span >= 0 && span <= 7)
            {
                rows[index].FontColor = fontColor;
                rows[index + 1].FontColor = fontColor;
            }
        }
        else
        {
            // Preserve CheckifSevendays' final comparison against row(count).
            for (int x = 0; x <= count; x++)
            {
                int plus = x == count ? count : x + 1;
                int dateIndex = x == count ? count : index + plus;
                var nextDate = ParseDate(rows[dateIndex].Timeofrequest);
                var span = (baseDate - nextDate).Days;
                if (span >= 0 && span <= 7)
                {
                    if (x == 0) rows[index].FontColor = fontColor;
                    rows[index + plus].FontColor = fontColor;
                }
            }
        }
    }

    private static DateTime ParseDate(string s)
        => DateTime.ParseExact(s, "MM/dd/yyyy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);

    // Font-color lookup (mirrors VB GetFontColor)
    private static string GetFontColor(int id, string process) => process switch
    {
        "EDS" => id == 13 ? "Blue" : id == 16 ? "Red" : "Black",
        "DC" => id == 48 ? "Blue" : id == 49 ? "Red" : "Black",
        "DB" => id == 18 ? "Blue" : id == 19 ? "Red" : "Black",
        "WB" => id == 45 ? "Blue" : id == 46 ? "Red" : "Black",
        "MP" => id == 24 ? "Blue" : id == 25 ? "Red" : "Black",
        "PL" => id == 42 ? "Blue" : id == 43 ? "Red" : "Black",
        "FL" => id == 1 ? "Blue" : id == 2 ? "Red" : "Black",
        "FT" => id == 31 ? "Blue" : id == 33 ? "Red" : "Black",
        "TP" => id == 9 ? "Blue" : id == 30 ? "Red" : "Black",
        "MEMS" => id == 72 ? "Blue" : id == 74 ? "Red" : "Black",
        // Form2 leaves the color empty for ATC/SING; those repeats still qualify.
        _ => ""
    };

    // Board cell builder (mirrors VB InputToLabel)
    /// <summary>
    /// Maps (process, location, mcno) → cell key and increments count/list.
    /// Existing API keys, query locations and designer columns are mapped
    /// together in AlertBoardConfig; suffixes are not query-loop indices.
    /// </summary>
    private static void AddToBoard(AlertBoardData board, string process, string location, string mcno)
    {
        if (!AlertBoardConfig.CellKeys.TryGetValue((process, location), out var key)) return;

        if (!board.Cells.TryGetValue(key, out var cell))
        {
            cell = new AlertCell();
            board.Cells[key] = cell;
        }
        cell.Count++;
        if (!cell.Machines.Contains(mcno))
            cell.Machines.Add(mcno);
    }

    // Location totals (mirrors VB TotalLabel)
    private static void BuildTotals(AlertBoardData board)
    {
        foreach (var column in AlertBoardConfig.DisplayColumns)
            board.LocationTotals[column] = board.Cells
                .Where(cell => AlertBoardConfig.Cells[cell.Key].Column == column)
                .Sum(cell => cell.Value.Count);
        board.GrandTotal = board.Cells.Values.Sum(cell => cell.Count);
    }
}
