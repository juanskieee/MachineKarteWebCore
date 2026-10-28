using System;
using System.Linq;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages
{
    public class McdMonitoringModel : PageModel
    {
        private readonly McdMonitoringService _service;

        public McdMonitoringModel(McdMonitoringService service)
        {
            _service = service;
        }

        public void OnGet() { }

        // ─────────────────────────────────────────────────────────────────────
        // GET /McdMonitoring?handler=Dashboard&DateFrom=...&DateTo=...&UserRole=...
        // Returns McdDashboardResult
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnGetDashboardAsync(
            string dateFrom, string dateTo, string userRole)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dateFrom) || string.IsNullOrWhiteSpace(dateTo))
                    return new JsonResult(AjaxResult.Fail("Date range is required."));

                // Normalize datetime strings coming from datetime-local input
                // (format: "yyyy-MM-ddTHH:mm" → "MM/dd/yyyy HH:mm:ss")
                string from = NormalizeDate(dateFrom);
                string to = NormalizeDate(dateTo);

                var filter = new McdMonitoringFilter
                {
                    DateFrom = from,
                    DateTo = to,
                    UserRole = userRole ?? string.Empty
                };

                var result = await _service.BuildDashboardAsync(filter);
                return new JsonResult(AjaxResult.Ok(result));
            }
            catch (Exception ex)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET /McdMonitoring?handler=MachineList&cellKey=DC_1&DateFrom=...&...
        // Returns machine list for a clicked cell
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnGetMachineListAsync(
            string cellKey, string dateFrom, string dateTo, string userRole)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cellKey))
                    return new JsonResult(AjaxResult.Fail("Cell key is required."));

                var (process, location) = McdMonitoringService.ResolveProcessLocation(cellKey);

                if (string.IsNullOrWhiteSpace(process) || string.IsNullOrWhiteSpace(location))
                    return new JsonResult(AjaxResult.Fail("Unknown cell key."));

                string from = NormalizeDate(dateFrom);
                string to = NormalizeDate(dateTo);
                bool isTe = userRole == "TE";

                // Re-run the query + analysis for just this one cell
                var filter = new McdMonitoringFilter
                {
                    DateFrom = from,
                    DateTo = to,
                    UserRole = userRole ?? string.Empty
                };

                // Use the full dashboard result and pick the matching cell
                var dashboard = await _service.BuildDashboardAsync(filter);
                var cell = dashboard.Cells
                    .FirstOrDefault(c => c.Process == process && c.Location == location);

                var res = new McdMachineListResult
                {
                    Process = process,
                    Location = location,
                    Machines = cell?.Machines ?? new System.Collections.Generic.List<string>()
                };

                return new JsonResult(AjaxResult.Ok(res));
            }
            catch (Exception ex)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string NormalizeDate(string raw)
        {
            // datetime-local gives "yyyy-MM-ddTHH:mm" or "yyyy-MM-ddTHH:mm:ss"
            if (DateTime.TryParse(raw, out var dt))
                return dt.ToString("MM/dd/yyyy HH:mm:ss");

            return raw; // pass through if already formatted
        }
    }
}