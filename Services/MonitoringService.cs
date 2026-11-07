// Services/MonitoringService.cs
// Converted from Form1.vb business logic — E-Karte System
// C# ASP.NET Core

using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class MonitoringService
    {
        private readonly MonitoringRepository _repo;
        private static readonly string[] FloorCodes =
        {
            "LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F",
            "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
        };

        public MonitoringService(MonitoringRepository repo)
        {
            _repo = repo;
        }

        // ----------------------------------------------------------------
        // Dropdown cascade
        // ----------------------------------------------------------------

        /// <summary>
        /// Process list per user type.
        /// Mirrors cbUser_SelectedIndexChanged in Form1.vb.
        /// </summary>
        public List<string> GetProcessList(string userType)
        {
            if (userType == "TE")
                return new List<string> { "-Select-", "FL", "FT", "TP", "EDS", "MEMS" };

            return new List<string>
            {
                "-Select-", "DC", "DB", "WB", "MP", "PL",
                "FL", "ATC", "SING", "FT", "TP", "EDS", "MEMS"
            };
        }

        /// <summary>
        /// Specific locations available after selecting a process.
        /// </summary>
        public List<string> GetFloorList() => new[] { "-Select-" }.Concat(FloorCodes).ToList();

        public static bool IsSpecificFloor(string? floor) =>
            floor is not null && FloorCodes.Contains(floor, StringComparer.Ordinal);

        /// <summary>
        /// Machine list for a given process+floor — DB-driven.
        /// Mirrors cbFloor_SelectedIndexChanged in Form1.vb.
        /// </summary>
        public async Task<List<string>> GetMachineListAsync(
            string userType, string process, string floor, CancellationToken cancellationToken = default)
        {
            if (!IsSpecificFloor(floor))
                throw new ArgumentException("Please select a specific location.");

            var machines = await _repo.GetMachinesAsync(userType, process, floor, cancellationToken);
            machines.Insert(0, "All");
            return machines;
        }

        // ----------------------------------------------------------------
        // Dashboard search (btnSearch_Click equivalent)
        // ----------------------------------------------------------------

        public async Task<MonitoringDashboardResult> SearchDashboardAsync(MonitoringFilter f, CancellationToken cancellationToken = default)
        {
            var summariesTask = MonitoringQueryBatch.RunAsync(
                () => _repo.GetPagedMachineSummariesAsync(f, cancellationToken, allPages: true), cancellationToken);
            var rowsTask = MonitoringQueryBatch.RunAsync(() => _repo.GetBMCardsAsync(
                f.UserType, f.Process, "All", "All", f.DateFrom, f.DateTo, cancellationToken: cancellationToken), cancellationToken);
            await Task.WhenAll(summariesTask, rowsTask);
            var (summaries, totalPages) = await summariesTask;
            var rows = await rowsTask;
            f.Page = Math.Clamp(f.Page, 1, totalPages);
            var rowsByFloor = rows.ToLookup(row => row.Location);
            var floors = GetProcessFloors(f.Process).Select(floor =>
            {
                var floorRows = rowsByFloor[floor].ToList();
                var repeats = floorRows.Where(row => row.NeedsAction).ToList();
                var needAction = floorRows.Where(row => row.IsMCD == "Yes" && row.HaveAction == "No").ToList();
                var withAction = floorRows.Where(row => row.IsMCD == "Yes" && row.HaveAction == "Yes").ToList();
                return new FloorSummary
                {
                    FloorCode = floor,
                    BMCount = floorRows.Count,
                    MachineCount = floorRows.Select(row => row.MachineNo).Distinct().Count(),
                    MCDCount = needAction.Count + withAction.Count,
                    RepeatBMCount = CountNeedActionGroups(floorRows),
                    NeedActionCount = needAction.Count,
                    WithActionCount = withAction.Count,
                    RepeatMachines = repeats.Select(row => row.MachineNo).Distinct().ToList(),
                    NeedActionMachines = needAction.Select(row => row.MachineNo).Distinct().ToList(),
                    WithActionMachines = withAction.Select(row => row.MachineNo).Distinct().ToList()
                };
            }).ToList();

            // Reuse the rows already read for the floor counters. A page change
            // then needs neither another search nor one query per machine.
            var columnRows = rows.Where(row => string.Equals(row.Location, f.Floor, StringComparison.OrdinalIgnoreCase))
                .ToLookup(row => (row.MachineNo, row.MachineType));
            var columns = summaries.Select(summary =>
            {
                var cards = columnRows[(summary.MachineNo, summary.MachineType)].ToList();
                return new MachineCardResult
                {
                    MachineNo = summary.MachineNo,
                    MachineType = summary.MachineType,
                    ActionLine = summary.ActionLine,
                    NeedActionCount = CountNeedActionGroups(cards),
                    BMCards = cards
                };
            }).ToList();

            return new MonitoringDashboardResult
            {
                MachineSummaries = summaries.Skip((f.Page - 1) * 10).Take(10).ToList(),
                MachineCards = columns,
                FloorSummaries = floors,
                CurrentPage = f.Page,
                TotalPages = totalPages
            };
        }

        // ----------------------------------------------------------------
        // Retained single-column API and parity reference for grouped loading.
        // ----------------------------------------------------------------

        public async Task<MachineCardResult> GetMachineCardAsync(
            MonitoringFilter f, string machineNo, string machineType, int actionLine, CancellationToken cancellationToken = default)
        {
            var rows = await MonitoringQueryBatch.RunAsync(() => _repo.GetBMCardsAsync(
                f.UserType, f.Process, f.Floor,
                machineNo, f.DateFrom, f.DateTo, machineType, cancellationToken), cancellationToken);

            return new MachineCardResult
            {
                MachineNo = machineNo,
                MachineType = machineType,
                ActionLine = actionLine,
                NeedActionCount = CountNeedActionGroups(rows),
                BMCards = rows
            };
        }

        private static int CountNeedActionGroups(IReadOnlyList<BMCardRow> rows) =>
            rows.Count(row => row.NeedsAction);

        // ----------------------------------------------------------------
        // PC status tracking (Form1_Load → UpdatePCStatus)
        // ----------------------------------------------------------------

        public async Task TrackSessionAsync(
            string pcName, string ipAddress, string appVersion, CancellationToken cancellationToken = default)
        {
            await _repo.UpsertPCStatusAsync(pcName, ipAddress, $"v.{appVersion}", cancellationToken);
        }

        // ----------------------------------------------------------------
        // Process → floor mapping for summary panel (Panel5 / LoadMCName1)
        // ----------------------------------------------------------------

        public string[] GetProcessFloors(string process) =>
            MonitoringRepository.ProcessFloors(process);
    }
}
