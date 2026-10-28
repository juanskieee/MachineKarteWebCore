// Services/RawDataService.cs
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class RawDataService
    {
        private readonly RawDataRepository _repo;

        // Frequency bands displayed / charted (2x … 10x)
        private static readonly string[] FrequencyBands =
            { "2x", "3x", "4x", "5x", "6x", "7x", "8x", "9x", "10x" };

        // Month abbreviation map (number → short label)
        private static readonly Dictionary<int, string> MonthAbbr = new()
        {
            {1,"Jan"},{2,"Feb"},{3,"Mar"},{4,"Apr"},{5,"May"},{6,"Jun"},
            {7,"Jul"},{8,"Aug"},{9,"Sep"},{10,"Oct"},{11,"Nov"},{12,"Dec"}
        };

        public RawDataService(RawDataRepository repo) => _repo = repo;

        // ── Public entry point ───────────────────────────────────────────────
        public RawDataResult Load(RawDataRequest req)
        {
            var tableName = GetTableName(req.Process);
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException($"Unknown process code: {req.Process}");

            var result = new RawDataResult();

            // ── Frequency analysis ───────────────────────────────────────────
            var freqRaw = _repo.GetFrequencyRawRows(tableName, req.Location,
                                                    req.DateFrom, req.DateTo);
            result.FrequencyTable = BuildFrequencyTable(freqRaw);
            result.FrequencyChart = BuildFrequencyChart(result.FrequencyTable);

            // ── BM-case monthly breakdown ────────────────────────────────────
            var bmRaw = _repo.GetBmCaseMonthlyRows(tableName, req.Location,
                                                   req.DateFrom, req.DateTo);
            result.BmCaseTable = BuildBmCaseTable(bmRaw);
            result.BmCaseChart = BuildBmCaseChart(result.BmCaseTable);

            return result;
        }

        // ── Frequency table builder ──────────────────────────────────────────
        /// <summary>
        /// Replicates the VB loop logic that walks the DataTable rows and emits
        /// "2x / 3x / 4x / 5x" rows into CreatedTable, then groups by
        /// (Frequency, DateStr) and sums counts.
        /// </summary>
        private static List<FrequencyRow> BuildFrequencyTable(
            System.Data.DataTable dt)
        {
            // Intermediate accumulator – mirrors the VB CreatedTable
            var interim = new List<(string Freq, string DateLabel,
                                    int DateNum, int DateYear)>();

            bool ifSame = false;
            int freqCnt = 0;

            for (int i = 0; i < dt.Rows.Count - 1; i++)
            {
                var row = dt.Rows[i];

                if (ifSame)
                {
                    // Still inside a run — skip until mcno changes
                    if (SameMcno(row, dt, i + 1))
                        continue;
                    freqCnt = 0;
                    ifSame = false;
                }
                else
                {
                    // Must share mcno with its own next row to qualify
                    if (!SameMcno(row, dt, i))
                    {
                        ifSame = false;
                        continue;
                    }

                    string freq = string.Empty;
                    bool found = false;

                    if (i + 4 < dt.Rows.Count)
                    {
                        if (SameCase(row, dt, i, 5)) { freq = "5x"; found = true; }
                        else if (SameCase(row, dt, i, 4)) { freq = "4x"; found = true; }
                        else if (SameCase(row, dt, i, 3)) { freq = "3x"; found = true; }
                        else if (SameCase(row, dt, i, 2)) { freq = "2x"; found = true; }
                    }
                    else if (i + 3 < dt.Rows.Count)
                    {
                        if (SameCase(row, dt, i, 4)) { freq = "4x"; found = true; }
                        else if (SameCase(row, dt, i, 3)) { freq = "3x"; found = true; }
                        else if (SameCase(row, dt, i, 2)) { freq = "2x"; found = true; }
                    }
                    else if (i + 2 < dt.Rows.Count)
                    {
                        if (SameCase(row, dt, i, 3)) { freq = "3x"; found = true; }
                        else if (SameCase(row, dt, i, 2)) { freq = "2x"; found = true; }
                    }
                    else if (i + 1 < dt.Rows.Count)
                    {
                        if (SameCase(row, dt, i, 2)) { freq = "2x"; found = true; }
                    }

                    if (found)
                    {
                        freqCnt++;
                        string label = $"{row["DateMonth"]} {row["DateYear"]}";
                        int dNum = Convert.ToInt32(row["DateNum"]);
                        int dYear = Convert.ToInt32(row["DateYear"]);
                        interim.Add((freq, label, dNum, dYear));
                        ifSame = true;
                    }
                }
            }

            // Group by (Frequency, DateLabel) and sum freqCnt — mirrors the VB
            // duplicates LINQ query that counts groups with >1 member.
            var grouped = interim
                .GroupBy(r => (r.Freq, r.DateNum, r.DateYear))
                .Where(g => g.Count() > 1)
                .Select(g => new FrequencyRow
                {
                    Frequency = g.Key.Freq,
                    DateLabel = g.First().DateLabel,
                    DateMonth = g.Key.DateNum,
                    DateYear = g.Key.DateYear,
                    Total = g.Count()
                })
                .OrderBy(r => r.DateYear)
                .ThenBy(r => r.DateMonth)
                .ThenByDescending(r => r.Total)
                .ToList();

            return grouped;
        }

        // ── BM-case table builder ────────────────────────────────────────────
        private static List<BmCaseRow> BuildBmCaseTable(System.Data.DataTable dt)
        {
            var list = new List<BmCaseRow>();
            foreach (System.Data.DataRow r in dt.Rows)
            {
                list.Add(new BmCaseRow
                {
                    BMCase = r["BMCase"].ToString()!,
                    BMCount = Convert.ToInt32(r["BMCount"]),
                    DateMonth = r["DateMonth"].ToString()!,
                    DateNum = Convert.ToInt32(r["DateNum"]),
                    DateYear = Convert.ToInt32(r["DateYear"])
                });
            }
            return list;
        }

        // ── Chart series builders ────────────────────────────────────────────
        private static List<ChartSeries> BuildFrequencyChart(
            List<FrequencyRow> table)
        {
            var result = new List<ChartSeries>();
            foreach (var band in FrequencyBands)
            {
                var pts = table
                    .Where(r => r.Frequency == band)
                    .Select(r => new ChartPoint
                    {
                        Label = $"{MonthAbbr[r.DateMonth]} {r.DateYear}",
                        Value = r.Total
                    }).ToList();

                if (pts.Any())
                    result.Add(new ChartSeries { Name = band, Points = pts });
            }
            return result;
        }

        private static List<ChartSeries> BuildBmCaseChart(List<BmCaseRow> table)
        {
            var result = new List<ChartSeries>();
            var cases = table.Select(r => r.BMCase).Distinct().ToList();

            foreach (var @case in cases)
            {
                var pts = table
                    .Where(r => r.BMCase == @case)
                    .Select(r => new ChartPoint
                    {
                        Label = $"{MonthAbbr[r.DateNum]} {r.DateYear}",
                        Value = r.BMCount
                    }).ToList();

                result.Add(new ChartSeries { Name = @case, Points = pts });
            }
            return result;
        }

        // ── Row-comparison helpers (mirror the VB AreNext* functions) ────────
        private static bool SameMcno(System.Data.DataRow row,
                                     System.Data.DataTable dt, int idx)
            => row["mcno"].Equals(dt.Rows[idx]["mcno"]);

        private static bool SameCase(System.Data.DataRow row,
                                     System.Data.DataTable dt,
                                     int startIdx, int count)
        {
            for (int i = startIdx; i < startIdx + count; i++)
            {
                if (i >= dt.Rows.Count) return false;
                if (!row["BMCase"].Equals(dt.Rows[i]["BMCase"])) return false;
                if (!row["mcno"].Equals(dt.Rows[i]["mcno"])) return false;
            }
            return true;
        }

        // ── Process → table name map ─────────────────────────────────────────
        private static string GetTableName(string process) => process switch
        {
            "DC" => "DCBMMaintenance",
            "DB" => "DBBMMaintenance",
            "WB" => "WBBMMaintenance",
            "MP" => "MPBMMaintenance",
            "PL" => "PLBMMaintenance",
            "FL" => "FLBMMaintenance",
            "FT" => "FTBMMaintenance",
            "TP" => "TPBMMaintenance",
            "EDS" => "EDSBMMaintenance",
            "MEMS" => "MEMSBMaintenance",
            _ => string.Empty
        };
    }
}