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
        /// All floor options (shown in cbFloor after process is selected).
        /// Mirrors cbProcess_SelectedIndexChanged in Form1.vb.
        /// </summary>
        public List<string> GetFloorList() =>
            new()
            {
                "-Select-", "All",
                "LSI1-1F", "LSI1-2F",
                "LSI2-1F", "LSI2-2F", "LSI2-3F",
                "LSI3-1F", "LSI3-2F", "LSI3-3F", "LSI3-4F"
            };

        /// <summary>
        /// Machine list for a given process+floor — DB-driven.
        /// Mirrors cbFloor_SelectedIndexChanged in Form1.vb.
        /// </summary>
        public async Task<List<string>> GetMachineListAsync(
            string userType, string process, string floor)
        {
            if (floor == "All")
                return new List<string> { "All" };

            var machines = await _repo.GetMachinesAsync(userType, process, floor);
            machines.Insert(0, "All");
            return machines;
        }

        // ----------------------------------------------------------------
        // Dashboard search (btnSearch_Click equivalent)
        // ----------------------------------------------------------------

        public async Task<MonitoringDashboardResult> SearchDashboardAsync(MonitoringFilter f)
        {
            var (summaries, totalPages) = await _repo.GetPagedMachineSummariesAsync(f);
            if (f.Page > totalPages)
            {
                f.Page = totalPages;
                (summaries, totalPages) = await _repo.GetPagedMachineSummariesAsync(f);
            }
            var rows = await _repo.GetBMCardsAsync(
                f.UserType, f.Process, "All", "All", f.DateFrom, f.DateTo);
            var floors = GetProcessFloors(f.Process).Select(floor =>
            {
                var floorRows = rows.Where(row => row.Location == floor).ToList();
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

            return new MonitoringDashboardResult
            {
                MachineSummaries = summaries,
                FloorSummaries = floors,
                CurrentPage = f.Page,
                TotalPages = totalPages
            };
        }

        // ----------------------------------------------------------------
        // BM cards for a single machine column (AJAX per-card lazy load)
        // ----------------------------------------------------------------

        public async Task<MachineCardResult> GetMachineCardAsync(
            MonitoringFilter f, string machineNo, string machineType, int actionLine)
        {
            var rows = await _repo.GetBMCardsAsync(
                f.UserType, f.Process, f.Floor,
                machineNo, f.DateFrom, f.DateTo, machineType);

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
            string pcName, string ipAddress, string appVersion)
        {
            await _repo.UpsertPCStatusAsync(pcName, ipAddress, $"v.{appVersion}");
        }

        // ----------------------------------------------------------------
        // Process → floor mapping for summary panel (Panel5 / LoadMCName1)
        // ----------------------------------------------------------------

        public string[] GetProcessFloors(string process) =>
            MonitoringRepository.ProcessFloors(process);
    }
}
