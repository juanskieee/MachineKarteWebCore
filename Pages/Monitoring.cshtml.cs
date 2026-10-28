// Pages/Monitoring.cshtml.cs
// Converted from Form1.vb — E-Karte System
// C# ASP.NET Core Razor Pages

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Pages
{
    public class MonitoringModel : PageModel
    {
        private readonly MonitoringService _svc;
        private readonly IConfiguration _config;
        private readonly ILogger<MonitoringModel> _logger;

        public MonitoringModel(
            MonitoringService svc,
            IConfiguration config,
            ILogger<MonitoringModel> logger)
        {
            _svc = svc;
            _config = config;
            _logger = logger;
        }

        // ── Initial page values ──────────────────────────────────────
        // Mirrors Form1_Load date initialization (dt1 = -30 days, dt2 = tomorrow)

        public string DefaultDateFrom =>
            DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");

        public string DefaultDateTo =>
            DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");

        // ── GET ──────────────────────────────────────────────────────
        public async Task OnGetAsync()
        {
            // Track who opened the page (mirrors UpdatePCStatus)
            await TrackSessionAsync();
        }

        // ════════════════════════════════════════════════════════════
        // AJAX HANDLERS
        // All return JSON — called by Monitoring.js via fetch()
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns process list for the selected user type.
        /// Mirrors cbUser_SelectedIndexChanged.
        /// GET: /Monitoring?handler=ProcessList&userType=PM
        /// </summary>
        public IActionResult OnGetProcessList(string userType)
        {
            var list = _svc.GetProcessList(userType);
            return new JsonResult(new AjaxResult<List<string>>
            {
                Data = list
            });
        }

        /// <summary>
        /// Returns full floor list (after process is selected).
        /// Mirrors cbProcess_SelectedIndexChanged.
        /// GET: /Monitoring?handler=FloorList
        /// </summary>
        public IActionResult OnGetFloorList()
        {
            return new JsonResult(new AjaxResult<List<string>>
            {
                Data = _svc.GetFloorList()
            });
        }

        /// <summary>
        /// Returns machines for the selected floor.
        /// Mirrors cbFloor_SelectedIndexChanged.
        /// GET: /Monitoring?handler=MachineList&userType=PM&process=DC&floor=LSI1-1F
        /// </summary>
        public async Task<IActionResult> OnGetMachineListAsync(
            string userType, string process, string floor)
        {
            var list = await _svc.GetMachineListAsync(userType, process, floor);
            return new JsonResult(new AjaxResult<MachineListResult>
            {
                Data = new MachineListResult { Machines = list }
            });
        }

        /// <summary>
        /// Main search — returns paged machine summaries + floor summary counts.
        /// Mirrors btnSearch_Click → LoadMCName + LoadMCName1.
        /// POST: /Monitoring?handler=Search
        /// Body: MonitoringFilter JSON
        /// </summary>
        public async Task<IActionResult> OnPostSearchAsync(
            [FromBody] MonitoringFilter filter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filter.Machine))
                    return new JsonResult(new AjaxResult<MonitoringDashboardResult>
                    {
                        Success = false,
                        Message = "Please select a machine number."
                    });

                if (string.IsNullOrWhiteSpace(filter.Process) || filter.Process == "-Select-")
                    return new JsonResult(new AjaxResult<MonitoringDashboardResult>
                    {
                        Success = false,
                        Message = "Please select a process."
                    });

                // Reset page counter on new search (mirrors lblTimer.Text = "600" + lblPageNumber.Text = 1)
                filter.Page = filter.Page <= 0 ? 1 : filter.Page;

                var result = await _svc.SearchDashboardAsync(filter);
                return new JsonResult(new AjaxResult<MonitoringDashboardResult>
                {
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search failed");
                return new JsonResult(new AjaxResult<MonitoringDashboardResult>
                {
                    Success = false,
                    Message = "Search failed: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Returns BM card rows for one machine column.
        /// Called per card after the machine summary columns are rendered (lazy load).
        /// Mirrors uC_Test.LoadMain / strQuery in LoadMCName1.
        /// GET: /Monitoring?handler=MachineCard&...params...
        /// </summary>
        public async Task<IActionResult> OnGetMachineCardAsync(
            string userType, string process, string floor,
            string machineNo, string machineType, int actionLine,
            string dateFrom, string dateTo)
        {
            try
            {
                var filter = new MonitoringFilter
                {
                    UserType = userType,
                    Process = process,
                    Floor = floor,
                    DateFrom = dateFrom,
                    DateTo = dateTo
                };

                var result = await _svc.GetMachineCardAsync(
                    filter, machineNo, machineType, actionLine);

                return new JsonResult(new AjaxResult<MachineCardResult> { Data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Machine card load failed for {Machine}", machineNo);
                return new JsonResult(new AjaxResult<MachineCardResult>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // ── Private helpers ──────────────────────────────────────────

        private async Task TrackSessionAsync()
        {
            try
            {
                string pcName = Environment.MachineName;
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                string version = _config["AppVersion"] ?? "1.0.0";
                await _svc.TrackSessionAsync(pcName, ipAddress, version);
            }
            catch (Exception ex)
            {
                // Non-critical — log and swallow
                _logger.LogWarning(ex, "PC status tracking failed");
            }
        }
    }
}