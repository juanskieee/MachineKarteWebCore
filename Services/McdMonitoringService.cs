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
    /// Ports Form7's active code path:
    ///     LoadSearch() → ClearLabel() + LoadMCName() + TotalLabel()
    /// where NewLoadColor() → InputToLabel() counts a row when
    ///     isMCD = "Yes" AND HaveAction = "No"
    ///
    /// Form7's LoadColor / 7-day recurrence helpers are commented out in the
    /// VB source and therefore are intentionally not converted.
    /// </summary>
    public class McdMonitoringService
    {
        private readonly McdMonitoringRepository _repo;

        public McdMonitoringService(McdMonitoringRepository repo)
        {
            _repo = repo;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main entry point – builds the complete process × location grid
        // ─────────────────────────────────────────────────────────────────────

        public async Task<McdDashboardResult> BuildDashboardAsync(McdMonitoringFilter filter)
        {
            var (from, to) = ParseRange(filter.DateFrom, filter.DateTo);
            bool isTe = string.Equals(filter.UserRole, "TE", StringComparison.OrdinalIgnoreCase);

            var result = new McdDashboardResult
            {
                Mode = isTe ? "TE" : "EEE",
                Columns = McdMonitoringConfig.AllLocations
            };

            foreach (var col in McdMonitoringConfig.AllLocations)
                result.LocationTotals[col] = 0;

            // The designer keeps every process row visible; mode only limits queried locations.
            foreach (var process in McdMonitoringConfig.GetProcesses(false))
            {
                var row = new McdProcessRowResult { Process = process };

                // Start with every display column marked N/A.
                foreach (var col in McdMonitoringConfig.AllLocations)
                {
                    row.Cells[col] = new McdCellResult
                    {
                        Key = McdMonitoringConfig.CellKey(process, col),
                        Process = process,
                        Location = col,
                        NotApplicable = true
                    };
                }

                foreach (var location in McdMonitoringConfig.GetLocations(process, isTe))
                {
                    var machines = await _repo.GetMachinesAsync(process, location, from, to, isTe);

                    var cell = new McdCellResult
                    {
                        Key = McdMonitoringConfig.CellKey(process, location),
                        Process = process,
                        Location = location,
                        Count = machines.Count,
                        Machines = machines,
                        NotApplicable = false
                    };

                    row.Cells[location] = cell;
                    row.RowTotal += cell.Count;
                    result.LocationTotals[location] += cell.Count;
                }

                result.ProcessTotals[process] = row.RowTotal;
                result.GrandTotal += row.RowTotal;
                result.Rows.Add(row);
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Machine list for one clicked cell.  Queries only that cell instead of
        // rebuilding the whole dashboard.
        // ─────────────────────────────────────────────────────────────────────

        public async Task<McdMachineListResult> GetCellAsync(
            string process, string location, McdMonitoringFilter filter)
        {
            var (from, to) = ParseRange(filter.DateFrom, filter.DateTo);
            bool isTe = string.Equals(filter.UserRole, "TE", StringComparison.OrdinalIgnoreCase);

            var validLocations = McdMonitoringConfig.GetLocations(process, isTe);
            if (!validLocations.Contains(location))
            {
                throw new ArgumentException(
                    $"Location '{location}' is not valid for process '{process}'.");
            }

            var machines = await _repo.GetMachinesAsync(process, location, from, to, isTe);

            return new McdMachineListResult
            {
                Key = McdMonitoringConfig.CellKey(process, location),
                Process = process,
                Location = location,
                Machines = machines
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Resolve a cell key such as "DC_1" or "ATC_65".</summary>
        public static (string Process, string Location) ResolveCellKey(string cellKey)
            => McdMonitoringConfig.ResolveCellKey(cellKey);

        private static (DateTime From, DateTime To) ParseRange(string rawFrom, string rawTo)
        {
            if (!TryParseDate(rawFrom, out var from))
                throw new ArgumentException("Invalid Date From value.");
            if (!TryParseDate(rawTo, out var to))
                throw new ArgumentException("Invalid Date To value.");
            if (from > to)
                throw new ArgumentException("Date From must be earlier than Date To.");

            return (from, to);
        }

        private static bool TryParseDate(string raw, out DateTime value)
        {
            string[] formats =
            {
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy HH:mm",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd"
            };

            return DateTime.TryParseExact(
                       raw, formats, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out value)
                   || DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out value);
        }
    }
}
