using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Repositories
{
    public class AlertBoardRepository
    {
        private readonly string _conn;

        // ─── Domain maps ──────────────────────────────────────────────────────
        private static readonly string[] ProcessesNormal =
            { "DC", "DB", "WB", "MP", "PL", "FL", "FT", "TP", "MEMS", "EDS", "ATC", "SING" };

        private static readonly string[] ProcessesTE =
            { "FL", "FT", "TP", "MEMS", "EDS" };

        private static readonly string[] Locations =
        {
            "LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F",
            "LSI2-3F", "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
        };

        public AlertBoardRepository(IConfiguration cfg)
        {
            _conn = cfg.GetConnectionString("DBConnection")!;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Fetches all BM rows for the requested date range and user mode,
        /// applies the duplicate-case / 7-day / no-action-card filtering,
        /// and returns the aggregated board data.
        /// </summary>
        public async Task<AlertBoardData> LoadBoardAsync(AlertBoardRequest req)
        {
            var board = new AlertBoardData();

            string[] processes = req.User == "TE" ? ProcessesTE : ProcessesNormal;

            // Build per-user SQL fragments (mirrors VB AddQuery / ActionQuery)
            string addQuery = BuildAddQuery(req.User);
            string actionTable = BuildActionTable(req.User);

            foreach (string process in processes)
            {
                foreach (string location in Locations)
                {
                    string sql = req.User == "TE"
                        ? BuildQueryTE(process)
                        : BuildQueryNormal(process, addQuery, actionTable);

                    var rows = await FetchRowsAsync(sql, req.DateFrom, req.DateTo, location);

                    if (rows.Count == 0) continue;

                    // Add Color column then run duplicate / solve logic
                    var alertRows = ApplySolveLogic(rows);

                    // Map qualifying rows into board cells
                    if (req.User == "TE")
                        ApplyColorTE(alertRows, board);
                    else
                        ApplyColorNormal(alertRows, board);
                }
            }

            BuildTotals(board);
            return board;
        }

        /// <summary>Returns machine list for a specific cell (PROCESS_LOCINDEX).</summary>
        public List<string> GetCellMachines(AlertBoardData board, string cellKey)
            => board.Cells.TryGetValue(cellKey, out var cell) ? cell.Machines : new();

        // ─── SQL builders ─────────────────────────────────────────────────────

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
       FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
       b.BMCase AS BMCase, a.bmid,
       ISNULL((SELECT 'Yes' FROM {actionTable}
               WHERE bmid = a.bmID AND machineno = a.mcno),'No') AS HaveAction,
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
LEFT  JOIN [eMaintenanceSystemDB].[dbo].[{actionTable}] c
       ON a.bmID = c.bmID AND a.mcno = c.machineno
WHERE  (a.timeofrequest BETWEEN @Range1 AND @Range2)
  AND  (a.Pmcategoryid = '1')
  AND  {addQuery}
  AND  (a.bmcaseid <> 0)
  AND  (a.Location = @Location)
ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.BMCase";
        }

        private static string BuildQueryTE(string process)
        {
            return $@"
SELECT a.process, a.location, a.testerNo AS Mcno,
       FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
       b.bmteunit AS BMCase, a.bmid,
       ISNULL((SELECT 'Yes' FROM MachineKarte_ActionCard_TE WHERE bmid = a.bmID),'No') AS HaveAction,
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
LEFT  JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] c ON a.bmID = c.bmID
WHERE  (a.timeofrequest BETWEEN @Range1 AND @Range2)
  AND  a.process = '{process}'
  AND  (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
  AND  (a.BMStatusID NOT IN ('30','31','1','2','3','7','17'))
  AND  (a.bmunitid <> 0)
  AND  (a.Location = @Location)
ORDER BY a.testerNo DESC, a.MCType DESC, a.Timeofrequest DESC, b.bmteunit";
        }

        // ─── Data fetch ───────────────────────────────────────────────────────

        private async Task<List<AlertBoardRow>> FetchRowsAsync(
            string sql, string dateFrom, string dateTo, string location)
        {
            var list = new List<AlertBoardRow>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Range1", dateFrom);
            cmd.Parameters.AddWithValue("@Range2", dateTo);
            cmd.Parameters.AddWithValue("@Location", location);

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
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
                if (rdr.ColumnCount() > 0)
                    row.Priority = rdr.HasColumn("Priority") ? rdr["Priority"] as string : null;
                list.Add(row);
            }
            return list;
        }

        // ─── Solve / color logic (mirrors VB CheckIfSolve + GetDuplicateRowsByColumn) ──

        /// <summary>
        /// Port of VB CheckIfSolve → GetDuplicateRowsByColumn → AreOtherRowsSame.
        /// Sorts by Mcno DESC, BMCase ASC, Timeofrequest DESC then marks duplicate-case
        /// rows that have HaveAction=Yes and span ≥7 days as Color=Green.
        /// Returns the filtered list (non-duplicate rows are excluded from the alert board).
        /// </summary>
        private static List<AlertBoardRow> ApplySolveLogic(List<AlertBoardRow> rows)
        {
            if (rows.Count == 0) return rows;

            // Sort: Mcno DESC, BMCase ASC, Timeofrequest DESC
            rows.Sort((a, b) =>
            {
                int c = string.Compare(b.Mcno, a.Mcno, StringComparison.Ordinal);
                if (c != 0) return c;
                c = string.Compare(a.BMCase, b.BMCase, StringComparison.Ordinal);
                if (c != 0) return c;
                // Timeofrequest DESC
                var da = DateTime.ParseExact(a.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                var db2 = DateTime.ParseExact(b.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                return DateTime.Compare(db2, da);
            });

            string firstColumnDate = rows[0].Timeofrequest;

            // Separate duplicate-case rows from non-duplicate-case rows
            var duplicates = new List<AlertBoardRow>();
            var nonDuplicates = new List<AlertBoardRow>();

            int rowCount = rows.Count;
            for (int i = 0; i < rowCount - 1; i++)
            {
                var cur = rows[i];
                var next = rows[i + 1];

                if (i == 0) duplicates.Add(cur); // always include first

                if (cur.BMCase == next.BMCase)
                {
                    duplicates.Add(next);
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

            return duplicates;
        }

        private static void MarkSolvedRows(List<AlertBoardRow> table, string firstColumnDate)
        {
            // Build unique first-occurrence list (ordered by BMCase appearance)
            var uniqueFirst = new List<AlertBoardRow>();
            var seen = new HashSet<string>();
            foreach (var r in table)
            {
                if (seen.Add(r.BMCase))
                    uniqueFirst.Add(r);
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
                    var daysFromNow = (dateNow - dateY).TotalDays;

                    if (daysFromNow >= 7)
                    {
                        if (row.HaveAction == "Yes") row.Color = "Green";
                        break;
                    }

                    if (unique.Timeofrequest != row.Timeofrequest)
                    {
                        var dateRow = DateTime.ParseExact(row.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture);
                        if ((dateY - dateRow).TotalDays >= 7)
                        {
                            if (row.HaveAction == "Yes") row.Color = "Green";
                        }
                    }
                    break;
                }
            }
        }

        // ─── Font color / 7-day cascade (mirrors VB LoadColor / LoadColor_TE) ──

        private static void ApplyColorNormal(List<AlertBoardRow> rows, AlertBoardData board)
        {

            // Re-sort: Mcno DESC, Timeofrequest DESC, BMCase ASC
            rows.Sort((a, b) =>
            {
                int c = string.Compare(b.Mcno, a.Mcno, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                var da = DateTime.ParseExact(a.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                var db2 = DateTime.ParseExact(b.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                c = DateTime.Compare(db2, da);
                if (c != 0) return c;
                return string.Compare(a.BMCase, b.BMCase, StringComparison.OrdinalIgnoreCase);
            });

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
                    if (caseMatch == 0) continue;
                    CheckAndMarkSevenDays(rows, i, caseMatch, fontColor);
                    var r = rows[i];
                    if (r.FontColor != "Black" && r.FontColor != "None"
                        && r.HaveAction != "Yes" && r.Color != "Green")
                    {
                        AddToBoard(board, r.Process, r.Location, r.Mcno);
                        triggered = true;
                    }
                }
            }
        }

        private static void ApplyColorTE(List<AlertBoardRow> rows, AlertBoardData board)
        {
            // Re-sort: Mcno DESC, Timeofrequest DESC, BMCase ASC
            rows.Sort((a, b) =>
            {
                int c = string.Compare(b.Mcno, a.Mcno, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                var da = DateTime.ParseExact(a.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                var db2 = DateTime.ParseExact(b.Timeofrequest, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                c = DateTime.Compare(db2, da);
                if (c != 0) return c;
                return string.Compare(a.BMCase, b.BMCase, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.FontColor != "None") continue;
                if (row.Priority != "QUALITY BM") continue;

                const string fontColor = "Red";
                int remaining = rows.Count - 1 - i;
                int depth = Math.Min(remaining, 3);   // TE uses up to 3-ahead

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
                    if (caseMatch == 0) continue;
                    CheckAndMarkSevenDays(rows, i, caseMatch, fontColor);
                    var r = rows[i];
                    if (r.FontColor != "Black" && r.FontColor != "None"
                        && r.HaveAction != "Yes" && r.Color != "Green")
                    {
                        AddToBoard(board, r.Process, r.Location, r.Mcno);
                        triggered = true;
                    }
                }
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

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
                var span = (baseDate - nextDate).TotalDays;
                if (span >= 0 && span <= 7)
                {
                    rows[index].FontColor = fontColor;
                    rows[index + 1].FontColor = fontColor;
                }
            }
            else
            {
                for (int plus = 1; plus <= count; plus++)
                {
                    int targetIdx = Math.Min(index + plus, rows.Count - 1);
                    var nextDate = ParseDate(rows[targetIdx].Timeofrequest);
                    var span = (baseDate - nextDate).TotalDays;
                    if (span >= 0 && span <= 7)
                    {
                        if (plus == 1) rows[index].FontColor = fontColor;
                        rows[targetIdx].FontColor = fontColor;
                    }
                }
            }
        }

        private static DateTime ParseDate(string s)
            => DateTime.ParseExact(s, "MM/dd/yyyy HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture);

        // ─── Font-color lookup (mirrors VB GetFontColor) ──────────────────────

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
            _ => "Black"
        };

        // ─── Board cell builder (mirrors VB InputToLabel) ─────────────────────

        /// <summary>
        /// Maps (process, location, mcno) → cell key and increments count/list.
        /// Cell keys follow the pattern "PROCESS_LOCINDEX" where LOCINDEX is the
        /// 0-based index in the Locations array above.
        /// </summary>
        private static void AddToBoard(AlertBoardData board, string process, string location, string mcno)
        {
            string? key = (process, location) switch
            {
                // DC
                ("DC", "LSI1-1F") => "DC_0",
                ("DC", "LSI2-1F") => "DC_2",
                ("DC", "LSI2-2F") => "DC_3",
                ("DC", "LSI2-3F") => "DC_4",
                ("DC", "LSI3-2F") => "DC_6",
                // DB
                ("DB", "LSI1-1F") => "DB_0",
                ("DB", "LSI2-1F") => "DB_2",
                ("DB", "LSI2-2F") => "DB_3",
                ("DB", "LSI2-3F") => "DB_4",
                ("DB", "LSI3-2F") => "DB_6",
                // WB
                ("WB", "LSI1-1F") => "WB_0",
                ("WB", "LSI2-1F") => "WB_2",
                ("WB", "LSI2-2F") => "WB_3",
                ("WB", "LSI2-3F") => "WB_4",
                ("WB", "LSI3-2F") => "WB_6",
                // MP
                ("MP", "LSI1-1F") => "MP_0",
                ("MP", "LSI2-1F") => "MP_2",
                ("MP", "LSI2-2F") => "MP_3",
                ("MP", "LSI2-3F") => "MP_4",
                ("MP", "LSI3-2F") => "MP_6",
                // PL
                ("PL", "LSI1-1F") => "PL_0",
                ("PL", "LSI2-1F") => "PL_2",
                ("PL", "LSI2-2F") => "PL_3",
                ("PL", "LSI2-3F") => "PL_4",
                ("PL", "LSI3-1F") => "PL_5",
                // ATC (LSI3-1F → cell "65" in VB)
                ("ATC", "LSI1-1F") => "ATC_0",
                ("ATC", "LSI1-2F") => "ATC_1",
                ("ATC", "LSI2-1F") => "ATC_2",
                ("ATC", "LSI2-2F") => "ATC_3",
                ("ATC", "LSI2-3F") => "ATC_4",
                ("ATC", "LSI3-1F") => "ATC_65",
                ("ATC", "LSI3-3F") => "ATC_6",
                // FL
                ("FL", "LSI1-2F") => "FL_1",
                ("FL", "LSI2-1F") => "FL_2",
                ("FL", "LSI2-2F") => "FL_3",
                ("FL", "LSI2-3F") => "FL_4",
                ("FL", "LSI3-3F") => "FL_5",
                // SING
                ("SING", "LSI1-2F") => "SING_1",
                ("SING", "LSI2-1F") => "SING_2",
                ("SING", "LSI2-2F") => "SING_3",
                ("SING", "LSI2-3F") => "SING_4",
                ("SING", "LSI3-3F") => "SING_5",
                // FT
                ("FT", "LSI1-2F") => "FT_1",
                ("FT", "LSI2-1F") => "FT_2",
                ("FT", "LSI2-2F") => "FT_3",
                ("FT", "LSI2-3F") => "FT_4",
                ("FT", "LSI3-3F") => "FT_5",
                // TP
                ("TP", "LSI1-2F") => "TP_1",
                ("TP", "LSI2-1F") => "TP_2",
                ("TP", "LSI2-2F") => "TP_3",
                ("TP", "LSI2-3F") => "TP_4",
                ("TP", "LSI3-3F") => "TP_5",
                // EDS
                ("EDS", "LSI3-4F") => "EDS_1",
                // MEMS
                ("MEMS", "LSI3-2F") => "MEMS_1",
                _ => null
            };

            if (key == null) return;

            if (!board.Cells.TryGetValue(key, out var cell))
            {
                cell = new AlertCell();
                board.Cells[key] = cell;
            }
            cell.Count++;
            if (!cell.Machines.Contains(mcno))
                cell.Machines.Add(mcno);
        }

        // ─── Location totals (mirrors VB TotalLabel) ──────────────────────────

        private static void BuildTotals(AlertBoardData board)
        {
            int Sum(params string[] keys)
            {
                int t = 0;
                foreach (var k in keys)
                    if (board.Cells.TryGetValue(k, out var c)) t += c.Count;
                return t;
            }

            board.LocationTotals["LSI1-1F"] = Sum("DC_0", "DB_0", "WB_0", "MP_0", "PL_0", "ATC_0");
            board.LocationTotals["LSI1-2F"] = Sum("FL_1", "FT_1", "TP_1", "ATC_1", "SING_1");
            board.LocationTotals["LSI2-1F"] = Sum("DC_2", "DB_2", "WB_2", "MP_2", "PL_2", "FL_2", "FT_2", "TP_2", "ATC_2", "SING_2");
            board.LocationTotals["LSI2-2F"] = Sum("DC_3", "DB_3", "WB_3", "MP_3", "PL_3", "FL_3", "FT_3", "TP_3", "ATC_3", "SING_3");
            board.LocationTotals["LSI2-3F"] = Sum("DC_4", "DB_4", "WB_4", "MP_4", "PL_4", "FL_4", "FT_4", "TP_4", "ATC_4", "SING_4");
            board.LocationTotals["LSI3-1F"] = Sum("PL_5", "ATC_65");
            board.LocationTotals["LSI3-2F"] = Sum("DC_6", "DB_6", "WB_6", "MP_6");
            board.LocationTotals["LSI3-3F"] = Sum("ATC_6", "FL_5", "SING_5", "FT_5", "TP_5");
            board.LocationTotals["LSI3-4F"] = Sum("EDS_1");

            // TE extra (MEMS at LSI3-2F already included above)
            board.LocationTotals["LSI3-2F"] += Sum("MEMS_1");

            board.GrandTotal = 0;
            foreach (var v in board.LocationTotals.Values) board.GrandTotal += v;
        }
    }

    // ─── SqlDataReader extension (used in FetchRowsAsync) ────────────────────

    internal static class SqlDataReaderExtensions
    {
        public static bool HasColumn(this SqlDataReader reader, string name)
        {
            for (int i = 0; i < reader.FieldCount; i++)
                if (reader.GetName(i).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        // Shim so ColumnCount() compiles (IDataRecord has FieldCount)
        public static int ColumnCount(this SqlDataReader reader) => reader.FieldCount;
    }
}