using System;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages
{
    public class MachineSummaryModel : PageModel
    {
        private readonly MachineSummaryService _service;

        public MachineSummaryModel(MachineSummaryService service) => _service = service;

        // Default date bounds shown on first load
        public string DefaultFrom { get; private set; } = string.Empty;
        public string DefaultTo { get; private set; } = string.Empty;

        public void OnGet()
        {
            // Default: today 00:00 → now
            var now = DateTime.Now;
            DefaultFrom = now.Date.ToString("yyyy-MM-dd") + "T00:00";
            DefaultTo = now.ToString("yyyy-MM-ddTHH:mm");
        }

        // ── AJAX: GET /MachineSummary?handler=Summary&dateFrom=...&dateTo=...&userMode=EEE
        public async Task<IActionResult> OnGetSummaryAsync(
            string dateFrom, string dateTo, string userMode = "EEE")
        {
            try
            {
                var query = new MachineSummaryQuery
                {
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    UserMode = userMode
                };
                var result = await _service.BuildSummaryAsync(query);
                return new JsonResult(AjaxResult.Ok(result));
            }
            catch (Exception ex)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }
    }
}