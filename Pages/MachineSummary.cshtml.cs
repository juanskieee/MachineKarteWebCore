using System;
using System.Globalization;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages
{
    public class MachineSummaryModel : WorkflowPageModel
    {
        private readonly MachineSummaryService _service;

        public MachineSummaryModel(MachineSummaryService service) => _service = service;

        public string DefaultFrom { get; private set; } = string.Empty;
        public string DefaultTo { get; private set; } = string.Empty;
        public string DefaultMode { get; private set; } = "EEE";

        public void OnGet(
            string? dateFrom = null,
            string? dateTo = null,
            string? userMode = null,
            string? userType = null)
        {
            var now = DateTime.Now;

            DefaultFrom = ParseOrDefault(dateFrom, now.AddDays(-30))
                .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            DefaultTo = ParseOrDefault(dateTo, now.AddDays(1))
                .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

            var mode = string.IsNullOrWhiteSpace(userMode) ? userType : userMode;
            DefaultMode = string.Equals(mode, "TE", StringComparison.OrdinalIgnoreCase)
                ? "TE"
                : "EEE";
        }

        public async Task<IActionResult> OnGetSummaryAsync(
            string dateFrom, string dateTo, string userMode = "EEE")
        {
            try
            {
                var result = await _service.BuildSummaryAsync(new MachineSummaryQuery
                {
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    UserMode = userMode
                });

                return new JsonResult(AjaxResult.Ok(result));
            }
            catch (Exception ex)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }

        public async Task<IActionResult> OnGetMachineListAsync(
            string process, string location, string dateFrom, string dateTo,
            string userMode = "EEE")
        {
            try
            {
                var result = await _service.GetCellAsync(process, location, new MachineSummaryQuery
                {
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    UserMode = userMode
                });

                return new JsonResult(AjaxResult.Ok(result));
            }
            catch (Exception ex)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }

        private static DateTime ParseOrDefault(string? raw, DateTime fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            string[] formats =
            {
                "yyyy-MM-dd",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-ddTHH:mm:ss",
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy HH:mm",
                "yyyy-MM-dd HH:mm:ss"
            };

            if (DateTime.TryParseExact(
                    raw, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var exact))
            {
                return exact;
            }

            return DateTime.TryParse(
                raw, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
