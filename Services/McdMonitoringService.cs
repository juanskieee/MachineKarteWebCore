using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    /// <summary>
    /// Ports Form7's LoadMCName / NewLoadColor / InputToLabel / TotalLabel / helper functions.
    /// All heavy processing (7-day window detection, colour rules, count accumulation) is done here.
    /// </summary>
    public class McdMonitoringService
    {
        private readonly McdMonitoringRepository _repo;

        // ─── Process / location maps ─────────────────────────────────────────

        private static readonly string[] ProcessesStandard =
            { "DC", "DB", "WB", "MP", "PL", "FL", "FT", "TP", "MEMS", "EDS", "ATC", "SING" };

        private static readonly string[] ProcessesTe =
            { "FL", "FT", "TP", "MEMS", "EDS" };

        // Maps process → ordered locations (matches VB ReDim logic)
        private static readonly Dictionary<string, string[]> LocationsStandard = new()
        {
            ["DC"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["DB"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["WB"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["MP"] = new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["PL"] = new[] { "LSI1-1F", "LSI2-1F" },
            ["FL"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["FT"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["TP"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["EDS"] = new[] { "LSI3-4F" },
            ["MEMS"] = new[] { "LSI3-2F" },
            ["ATC"] = new[] { "LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F", "LSI3-3F" },
            ["SING"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
        };

        private static readonly Dictionary<string, string[]> LocationsTe = new()
        {
            ["FL"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["FT"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["TP"] = new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F" },
            ["EDS"] = new[] { "LSI3-4F" },
            ["MEMS"] = new[] { "LSI3-2F" },
        };

        // ─── BMContentID → Meka / Quality / N/A maps ─────────────────────────

        private static readonly Dictionary<string, (int Meka, int Quality)> ContentIdMap = new()
        {
            ["EDS"] = (13, 16),
            ["DC"] = (48, 49),
            ["DB"] = (18, 19),
            ["WB"] = (45, 46),
            ["MP"] = (24, 25),
            ["PL"] = (42, 43),
            ["FL"] = (1, 2),
            ["FT"] = (31, 33),
            ["TP"] = (9, 30),
            ["MEMS"] = (72, 74),
        };

        public McdMonitoringService(McdMonitoringRepository repo)
        {
            _repo = repo;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main entry point – builds the full dashboard result
        // ─────────────────────────────────────────────────────────────────────

        public async Task<McdDashboardResult> BuildDashboardAsync(McdMonitoringFilter filter)
        {
            bool isTe = filter.UserRole == "TE";
            var processes = isTe ? ProcessesTe : ProcessesStandard;
            var locationMap = isTe ? LocationsTe : LocationsStandard;

            // Accumulate counts per (process, location)
            var cellMap = new Dictionary<string, McdCellCount>();

            foreach (var process in processes)
            {
                if (!locationMap.TryGetValue(process, out var locations)) continue;

                foreach (var location in locations)
                {
                    var rows = await _repo.GetBmRowsAsync(
                        process, location, filter.DateFrom, filter.DateTo, isTe);

                    if (rows.Count == 0) continue;

                    // Apply the 7-day window + colour analysis (ports NewLoadColor)
                    NewLoadColor(rows);

                    // Accumulate cells (ports InputToLabel)
                    foreach (var row in rows)
                    {
                        if (row.IsMcd != "Yes") continue;
                        if (row.HaveAction == "Yes") continue;
                        if (row.Color == "Green") continue;

                        string key = $"{row.Process}|{row.Location}";
                        if (!cellMap.TryGetValue(key, out var cell))
                        {
                            cell = new McdCellCount
                            {
                                Process = row.Process,
                                Location = row.Location
                            };
                            cellMap[key] = cell;
                        }

                        cell.Count++;
                        cell.Machines.Add(row.Mcno);
                    }
                }
            }

            var cells = cellMap.Values.ToList();

            // ─── Build totals (mirrors TotalLabel in VB) ─────────────────────

            int CellCount(string proc, string loc)
            {
                string key = $"{proc}|{loc}";
                return cellMap.TryGetValue(key, out var c) ? c.Count : 0;
            }

            var result = new McdDashboardResult { Cells = cells };

            // LSI1-1F: DC+DB+WB+MP+PL
            result.TotalLsi1_1F =
                CellCount("DC", "LSI1-1F") + CellCount("DB", "LSI1-1F") +
                CellCount("WB", "LSI1-1F") + CellCount("MP", "LSI1-1F") +
                CellCount("PL", "LSI1-1F");

            // LSI1-2F: FL+FT+TP
            result.TotalLsi1_2F =
                CellCount("FL", "LSI1-2F") + CellCount("FT", "LSI1-2F") + CellCount("TP", "LSI1-2F");

            // LSI2-1F: all processes that have LSI2-1F
            result.TotalLsi2_1F =
                CellCount("DC", "LSI2-1F") + CellCount("DB", "LSI2-1F") +
                CellCount("WB", "LSI2-1F") + CellCount("MP", "LSI2-1F") +
                CellCount("PL", "LSI2-1F") + CellCount("FL", "LSI2-1F") +
                CellCount("FT", "LSI2-1F") + CellCount("TP", "LSI2-1F");

            // LSI2-2F
            result.TotalLsi2_2F =
                CellCount("DC", "LSI2-2F") + CellCount("DB", "LSI2-2F") +
                CellCount("WB", "LSI2-2F") + CellCount("MP", "LSI2-2F") +
                CellCount("PL", "LSI2-2F") + CellCount("FL", "LSI2-2F") +
                CellCount("FT", "LSI2-2F") + CellCount("TP", "LSI2-2F");

            // LSI2-3F
            result.TotalLsi2_3F =
                CellCount("DC", "LSI2-3F") + CellCount("DB", "LSI2-3F") +
                CellCount("WB", "LSI2-3F") + CellCount("MP", "LSI2-3F") +
                CellCount("PL", "LSI2-3F") + CellCount("FL", "LSI2-3F") +
                CellCount("FT", "LSI2-3F") + CellCount("TP", "LSI2-3F");

            result.TotalLsi3_1F =
                CellCount("ATC", "LSI3-1F") + CellCount("MP", "LSI3-1F") +
                CellCount("PL", "LSI3-1F");

            // LSI3 combined (MEMS + FT@LSI3-2F + TP@LSI3-2F + MP@LSI3-2F – matches lblTotal_6 in VB)
            result.TotalLsi3 =
                CellCount("MEMS", "LSI3-2F") +
                CellCount("FT", "LSI3-2F") + CellCount("TP", "LSI3-2F") + CellCount("MP", "LSI3-2F");

            result.TotalLsi3_3F =
                CellCount("ATC", "LSI3-3F") + CellCount("FL", "LSI3-3F") +
                CellCount("FT", "LSI3-3F") + CellCount("TP", "LSI3-3F") +
                CellCount("SING", "LSI3-3F");

            // EDS only
            result.TotalLsi3_4F = CellCount("EDS", "LSI3-4F");

            // Grand total
            result.GrandTotal = cells.Sum(c => c.Count);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Resolve process+location from the label name encoded as "PROCESS_N"
        // (ports checkProcessAndLocation in VB)
        // ─────────────────────────────────────────────────────────────────────

        public static (string Process, string Location) ResolveProcessLocation(string cellKey)
        {
            // cellKey is e.g. "DC_1", "MP_5", "EDS_1"
            var parts = cellKey.Split('_');
            string proc = parts[0];
            string idx = parts.Length > 1 ? parts[1] : "1";

            string loc = (proc, idx) switch
            {
                ("DC", "1") => "LSI1-1F",
                ("DC", "2") => "LSI2-1F",
                ("DC", "3") => "LSI2-2F",
                ("DC", "4") => "LSI2-3F",

                ("DB", "1") => "LSI1-1F",
                ("DB", "2") => "LSI2-1F",
                ("DB", "3") => "LSI2-2F",
                ("DB", "4") => "LSI2-3F",

                ("WB", "1") => "LSI1-1F",
                ("WB", "2") => "LSI2-1F",
                ("WB", "3") => "LSI2-2F",
                ("WB", "4") => "LSI2-3F",

                ("MP", "1") => "LSI1-1F",
                ("MP", "2") => "LSI2-1F",
                ("MP", "3") => "LSI2-2F",
                ("MP", "4") => "LSI2-3F",
                ("MP", "5") => "LSI3-2F",
                ("MP", "6") => "LSI3-4F",

                ("PL", "1") => "LSI1-1F",
                ("PL", "2") => "LSI2-1F",
                ("PL", "3") => "LSI2-2F",
                ("PL", "4") => "LSI2-3F",

                ("FT", "1") => "LSI1-2F",
                ("FT", "2") => "LSI2-1F",
                ("FT", "3") => "LSI2-2F",
                ("FT", "4") => "LSI2-3F",

                ("FL", "1") => "LSI1-2F",
                ("FL", "2") => "LSI2-1F",
                ("FL", "3") => "LSI2-2F",
                ("FL", "4") => "LSI2-3F",

                ("TP", "1") => "LSI1-2F",
                ("TP", "2") => "LSI2-1F",
                ("TP", "3") => "LSI2-2F",
                ("TP", "4") => "LSI2-3F",

                ("EDS", "1") => "LSI3-4F",
                ("MEMS", "1") => "LSI3-2F",
                ("ATC", "1") => "LSI1-1F",
                ("ATC", "2") => "LSI1-2F",
                ("ATC", "3") => "LSI2-1F",
                ("ATC", "4") => "LSI2-2F",
                ("ATC", "5") => "LSI2-3F",
                ("ATC", "65") => "LSI3-1F",
                ("ATC", "6") => "LSI3-3F",
                ("SING", "1") => "LSI1-2F",
                ("SING", "2") => "LSI2-1F",
                ("SING", "3") => "LSI2-2F",
                ("SING", "4") => "LSI2-3F",
                ("SING", "5") => "LSI3-3F",

                _ => string.Empty
            };

            return (proc, loc);
        }

        // ─────────────────────────────────────────────────────────────────────
        // NewLoadColor – ports the complex 7-day window / font-colour logic
        // Mutates rows in-place (sets FontColor / Color).
        // ─────────────────────────────────────────────────────────────────────

        private void NewLoadColor(List<McdBmRow> rows)
        {
            int n = rows.Count;

            for (int i = 0; i < n; i++)
            {
                var row = rows[i];
                string proc = row.Process;
                int contentId = row.BmContentId;
                string category = IsQorM(contentId, proc);

                // Already coloured – skip
                if (row.FontColor != "None") continue;

                string fontColor = GetFontColor(contentId, proc);

                // ── Lookahead thresholds ────────────────────────────────────
                bool has4More = (i + 4) < n;
                bool has3More = (i + 3) < n;
                bool has2More = (i + 2) < n;
                bool has1More = (i + 1) < n;

                if (category == "N/A")
                {
                    // Non-Meka / non-Quality: just flag if isMCD and no action and not green
                    TryInputRow(rows, i);
                    continue;
                }

                if (has4More && rows[i].FontColor == "None")
                {
                    Apply7DayWindowed(rows, i, n, category, fontColor, 4, 3, 2, 1);
                }
                else if (has3More && rows[i].FontColor == "None")
                {
                    Apply7DayWindowed(rows, i, n, category, fontColor, 3, 2, 1, -1);
                }
                else if (has2More && rows[i].FontColor == "None")
                {
                    Apply7DayWindowed(rows, i, n, category, fontColor, 2, 1, -1, -1);
                }
                else if (has1More && rows[i].FontColor == "None")
                {
                    Apply7DayWindowed(rows, i, n, category, fontColor, 1, -1, -1, -1);
                }
                else
                {
                    // Last row
                    TryInputRow(rows, i);
                }
            }
        }

        /// <summary>
        /// Tries the widest matching window first and falls through to narrower ones.
        /// Mirrors the nested AreNextFour/Three/Two/OneColumnValuesEqual logic in VB.
        /// </summary>
        private static void Apply7DayWindowed(
            List<McdBmRow> rows, int i, int n,
            string category, string fontColor,
            int maxLook, int look3, int look2, int look1)
        {
            var cur = rows[i];

            for (int winSize = maxLook; winSize >= 1; winSize--)
            {
                if ((i + winSize) >= n)
                    continue;

                if (ContentMatchN(rows, i, winSize))
                {
                    if (CaseMatchN(rows, i, winSize))
                    {
                        CheckifSevendays(rows, fontColor, winSize, i);

                        if (rows[i].FontColor != "Black" &&
                            rows[i].FontColor != "None")
                        {
                            TryInputRow(rows, i);
                        }

                        return;
                    }

                    int caseWin = winSize - 1;

                    if (caseWin >= 1 &&
                        (i + caseWin) < n &&
                        CaseMatchN(rows, i, caseWin))
                    {
                        CheckifSevendays(rows, fontColor, caseWin, i);

                        if (rows[i].FontColor != "Black" &&
                            rows[i].FontColor != "None")
                        {
                            TryInputRow(rows, i);
                        }

                        return;
                    }
                }
            }

            TryInputRow(rows, i);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CheckifSevendays – ports VB function of same name.
        // Marks FontColor on rows within 7-day span.
        // ─────────────────────────────────────────────────────────────────────

        private static void CheckifSevendays(List<McdBmRow> rows, string fontColor, int count, int index)
        {
            const string fmt = "MM/dd/yyyy HH:mm:ss";

            if (count == 1)
            {
                var d1 = DateTime.ParseExact(rows[index].TimeOfRequest, fmt, CultureInfo.InvariantCulture);
                var d2 = DateTime.ParseExact(rows[index + 1].TimeOfRequest, fmt, CultureInfo.InvariantCulture);
                var span = d1 - d2;
                if (span.Days >= 0 && span.Days <= 7)
                {
                    rows[index].FontColor = fontColor;
                    rows[index + 1].FontColor = fontColor;
                }
            }
            else
            {
                for (int x = 0; x <= count; x++)
                {
                    int plus = x == count ? count - 1 : x;
                    if (index + plus >= rows.Count) break;

                    var d1 = DateTime.ParseExact(rows[index].TimeOfRequest, fmt, CultureInfo.InvariantCulture);
                    var d2 = DateTime.ParseExact(rows[index + plus].TimeOfRequest, fmt, CultureInfo.InvariantCulture);
                    var span = d1 - d2;

                    if (span.Days >= 0 && span.Days <= 7)
                    {
                        if (x == 0) rows[index].FontColor = fontColor;
                        rows[index + plus].FontColor = fontColor;
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IsQorM – mirrors VB function
        // ─────────────────────────────────────────────────────────────────────

        private static string IsQorM(int id, string process)
        {
            if (!ContentIdMap.TryGetValue(process, out var ids)) return "N/A";
            if (id == ids.Meka) return "Meka";
            if (id == ids.Quality) return "Quality";
            return "N/A";
        }

        // ─────────────────────────────────────────────────────────────────────
        // GetFontColor – mirrors VB function
        // ─────────────────────────────────────────────────────────────────────

        private static string GetFontColor(int id, string process)
        {
            if (!ContentIdMap.TryGetValue(process, out var ids)) return "Black";
            if (id == ids.Meka) return "Blue";
            if (id == ids.Quality) return "Red";
            return "Black";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Row comparison helpers (AreNextN…ColumnValuesEqual in VB)
        // ─────────────────────────────────────────────────────────────────────

        private static bool ContentMatchN(List<McdBmRow> rows, int i, int n)
        {
            for (int j = i + 1; j <= i + n && j < rows.Count; j++)
            {
                if (rows[i].Mcno != rows[j].Mcno) return false;
                if (rows[i].BmContentId != rows[j].BmContentId) return false;
            }
            return true;
        }

        private static bool CaseMatchN(List<McdBmRow> rows, int i, int n)
        {
            for (int j = i + 1; j <= i + n && j < rows.Count; j++)
            {
                if (rows[i].Mcno != rows[j].Mcno) return false;
                if (rows[i].BmCase != rows[j].BmCase) return false;
            }
            return true;
        }

        // Generic – kept for future use
        private static bool AreNextNEqual(List<McdBmRow> rows, int i, string column, int n)
        {
            object GetVal(McdBmRow r, string col) => col switch
            {
                "Mcno" => r.Mcno,
                "BMCase" => r.BmCase,
                "BMContentID_str" => r.BmContentId.ToString(),
                _ => string.Empty
            };

            object baseVal = GetVal(rows[i], column);
            for (int j = i + 1; j <= i + n && j < rows.Count; j++)
                if (!baseVal.Equals(GetVal(rows[j], column))) return false;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // TryInputRow – called when a row qualifies to be counted
        // (equivalent to InputToLabel calls scattered through VB)
        // ─────────────────────────────────────────────────────────────────────

        private static void TryInputRow(List<McdBmRow> rows, int i)
        {
            var r = rows[i];
            // The actual cell accumulation is done in BuildDashboardAsync after this returns.
            // This method exists as the equivalent gate to what VB called before InputToLabel.
            // The outer loop in BuildDashboardAsync checks IsMcd / HaveAction / Color itself.
        }
    }
}