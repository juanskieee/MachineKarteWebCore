using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class MachineSummaryService
    {
        private readonly MachineSummaryRepository _repo;

        public MachineSummaryService(MachineSummaryRepository repo) => _repo = repo;

        // ─────────────────────────────────────────────────────────────────────────
        //  Build the complete process × location grid.
        //  Mirrors Form11.LoadSearch() → ClearLabel() + LoadMCName() + TotalLabel().
        //  The active code path in the VB is NewLoadColor():
        //      if isMCD="Yes" AND HaveAction="No" → count the machine
        //  Both conditions are pushed into SQL via EXISTS / NOT EXISTS.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<MachineSummaryResult> BuildSummaryAsync(MachineSummaryQuery query)
        {
            if (!DateTime.TryParse(query.DateFrom, out var from))
                throw new ArgumentException("Invalid DateFrom");
            if (!DateTime.TryParse(query.DateTo, out var to))
                throw new ArgumentException("Invalid DateTo");

            bool isTE = string.Equals(query.UserMode, "TE", StringComparison.OrdinalIgnoreCase);

            var processOrder = isTE
                ? MachineSummaryConfig.TeProcessOrder
                : MachineSummaryConfig.EeeProcessOrder;

            var processMap = isTE
                ? MachineSummaryConfig.ProcessLocations_TE
                : MachineSummaryConfig.ProcessLocations;

            var result = new MachineSummaryResult
            {
                Columns = MachineSummaryConfig.DisplayColumns
            };

            // Pre-populate column totals dictionary
            foreach (var col in MachineSummaryConfig.DisplayColumns)
                result.LocationTotals[col] = 0;

            // ── Query each process row ─────────────────────────────────────────
            foreach (var proc in processOrder)
            {
                var row = new SummaryProcessRow { Process = proc };

                // Fill every display column – default to N/A
                foreach (var col in MachineSummaryConfig.DisplayColumns)
                {
                    row.Cells[col] = new SummaryCellData
                    {
                        Process = proc,
                        Location = col,
                        Count = 0,
                        NotApplicable = true   // will be cleared for valid locations
                    };
                }

                // If this process has no entry in the map, leave all cells N/A
                if (!processMap.TryGetValue(proc, out var locations))
                {
                    result.Rows.Add(row);
                    continue;
                }

                // Query each valid location in parallel for speed
                var tasks = new List<Task<(string loc, List<string> machines)>>();
                foreach (var loc in locations)
                {
                    var capturedLoc = loc;
                    tasks.Add(Task.Run(async () =>
                    {
                        var machines = isTE
                            ? await _repo.GetMachinesTEAsync(proc, capturedLoc, from, to)
                            : await _repo.GetMachinesAsync(proc, capturedLoc, from, to);
                        return (capturedLoc, machines);
                    }));
                }

                var results = await Task.WhenAll(tasks);

                foreach (var (loc, machines) in results)
                {
                    row.Cells[loc] = new SummaryCellData
                    {
                        Process = proc,
                        Location = loc,
                        Count = machines.Count,
                        Machines = machines,
                        NotApplicable = false
                    };
                    row.RowTotal += machines.Count;
                    result.LocationTotals[loc] += machines.Count;
                    result.GrandTotal += machines.Count;
                }

                result.Rows.Add(row);
            }

            return result;
        }
    }
}