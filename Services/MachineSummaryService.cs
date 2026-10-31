using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class MachineSummaryService
    {
        private readonly MachineSummaryRepository _repo;

        public MachineSummaryService(MachineSummaryRepository repo) => _repo = repo;

        public async Task<MachineSummaryResult> BuildSummaryAsync(MachineSummaryQuery query)
        {
            var (from, to) = ParseRange(query);
            var isTe = IsTe(query.UserMode);

            var result = new MachineSummaryResult
            {
                Mode = isTe ? "TE" : "EEE",
                Columns = MachineSummaryConfig.DisplayColumns
            };

            foreach (var column in MachineSummaryConfig.DisplayColumns)
                result.LocationTotals[column] = 0;

            foreach (var process in MachineSummaryConfig.GetProcesses(isTe))
            {
                var row = new SummaryProcessRow { Process = process };

                foreach (var column in MachineSummaryConfig.DisplayColumns)
                {
                    row.Cells[column] = new SummaryCellData
                    {
                        Process = process,
                        Location = column,
                        NotApplicable = true
                    };
                }

                foreach (var location in MachineSummaryConfig.GetLocations(process, isTe))
                {
                    var machines = await _repo.GetMachinesAsync(process, location, from, to, isTe);

                    row.Cells[location] = new SummaryCellData
                    {
                        Process = process,
                        Location = location,
                        Count = machines.Count,
                        Machines = machines,
                        NotApplicable = false
                    };

                    row.RowTotal += machines.Count;
                    result.LocationTotals[location] += machines.Count;
                }

                result.ProcessTotals[process] = row.RowTotal;
                result.GrandTotal += row.RowTotal;
                result.Rows.Add(row);
            }

            return result;
        }

        public async Task<MachineSummaryMachineListResult> GetCellAsync(
            string process, string location, MachineSummaryQuery query)
        {
            var (from, to) = ParseRange(query);
            var isTe = IsTe(query.UserMode);

            var locations = MachineSummaryConfig.GetLocations(process, isTe);
            if (!locations.Any(x => string.Equals(x, location, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Location '{location}' is not valid for process '{process}'.");
            }

            var machines = await _repo.GetMachinesAsync(process, location, from, to, isTe);

            return new MachineSummaryMachineListResult
            {
                Process = process,
                Location = location,
                Machines = machines
            };
        }

        private static bool IsTe(string userMode) =>
            string.Equals(userMode, "TE", StringComparison.OrdinalIgnoreCase);

        private static (DateTime From, DateTime To) ParseRange(MachineSummaryQuery query)
        {
            if (!TryParseDate(query.DateFrom, out var from))
                throw new ArgumentException("Invalid Date From value.");
            if (!TryParseDate(query.DateTo, out var to))
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
