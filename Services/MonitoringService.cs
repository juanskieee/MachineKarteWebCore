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
            var summaries = await _repo.GetPagedMachineSummariesAsync(f);
            var floors = await _repo.GetFloorSummariesAsync(
                                f.UserType, f.Process, f.DateFrom, f.DateTo);

            return new MonitoringDashboardResult
            {
                MachineSummaries = summaries,
                FloorSummaries = floors,
                CurrentPage = f.Page,
                TotalPages = 99   // Real total page count needs a COUNT query; stub for now
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
                machineNo, f.DateFrom, f.DateTo);

            return new MachineCardResult
            {
                MachineNo = machineNo,
                MachineType = machineType,
                ActionLine = actionLine,
                NeedActionCount = CountNeedActionGroups(rows),
                BMCards = rows
            };
        }

        private static int CountNeedActionGroups(IReadOnlyList<BMCardRow> rows)
        {
            var count = 0;

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (!IsNeedActionRepeat(row) ||
                    (index > 0 && IsSameRepeatGroup(rows[index - 1], row)))
                    continue;

                count++;
            }

            return count;
        }

        private static bool IsNeedActionRepeat(BMCardRow row) =>
            !string.Equals(row.FontColor, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.FontColor, "Black", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.HaveAction, "Yes", StringComparison.OrdinalIgnoreCase);

        private static bool IsSameRepeatGroup(BMCardRow previous, BMCardRow current) =>
            IsNeedActionRepeat(previous) &&
            string.Equals(previous.MachineNo, current.MachineNo,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previous.BMContentId, current.BMContentId,
                StringComparison.Ordinal) &&
            string.Equals(previous.BMCase, current.BMCase,
                StringComparison.Ordinal) &&
            string.Equals(previous.FontColor, current.FontColor,
                StringComparison.OrdinalIgnoreCase);

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