using System;
using System.Globalization;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages
{
    public class McdMonitoringModel : WorkflowPageModel
    {
        private readonly McdMonitoringService _service;

        public McdMonitoringModel(McdMonitoringService service)
        {
            _service = service;
        }

        public void OnGet() { }

        // ─────────────────────────────────────────────────────────────────────
        // GET /McdMonitoring?handler=Dashboard&dateFrom=...&dateTo=...&userRole=...
        // Returns the complete process × location grid.
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnGetDashboardAsync(
            string dateFrom, string dateTo, string userRole)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dateFrom) || string.IsNullOrWhiteSpace(dateTo))
                    return new JsonResult(AjaxResult.Fail("Date range is required."));

                var filter = new McdMonitoringFilter
                {
                    DateFrom = NormalizeDate(dateFrom),
                    DateTo = NormalizeDate(dateTo),
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
        // GET /McdMonitoring?handler=MachineList&cellKey=DC_1&dateFrom=...&...
        // Returns the machine list for one clicked cell.
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnGetMachineListAsync(
            string cellKey, string dateFrom, string dateTo, string userRole)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cellKey))
                    return new JsonResult(AjaxResult.Fail("Cell key is required."));

                var (process, location) = McdMonitoringService.ResolveCellKey(cellKey);

                if (string.IsNullOrWhiteSpace(process) || string.IsNullOrWhiteSpace(location))
                    return new JsonResult(AjaxResult.Fail("Unknown cell key."));

                var filter = new McdMonitoringFilter
                {
                    DateFrom = NormalizeDate(dateFrom),
                    DateTo = NormalizeDate(dateTo),
                    UserRole = userRole ?? string.Empty
                };

                var result = await _service.GetCellAsync(process, location, filter);
                return new JsonResult(AjaxResult.Ok(result));
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
            string[] formats =
            {
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy HH:mm",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd"
            };

            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                return dt.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return raw;
        }
    }
}
