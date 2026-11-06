using System;
using System.IO;
using System.Threading.Tasks;
using MachineKarte.Web.Services.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Controllers
{
    /// <summary>
    /// Form10 → TeEvidenceMaking.cshtml
    ///
    /// Monitoring opens the TE evidence partial from a TE card's E action.
    /// The Generate handler streams the workbook directly to that
    /// dialog, matching Form10.EMakingPrint without standalone navigation.
    /// </summary>
    public class TeEvidenceMakingController : WorkflowController
    {
        private readonly TeEvidenceMakingService _svc;

        public TeEvidenceMakingController(TeEvidenceMakingService svc)
        {
            _svc = svc;
        }

        [HttpGet, WorkflowHandler]
        public IActionResult Index() => WorkflowView();

        // ─── AJAX: generate TE evidence workbook ──────────────────────────────
        // GET /TeEvidenceMaking?handler=Generate&bmId=12345

        [HttpGet, WorkflowHandler("Generate")]
        public async Task<IActionResult> GenerateAsync(string bmId, string format = "xlsx", string process = "")
        {
            if (string.IsNullOrWhiteSpace(bmId))
                return new JsonResult(new { success = false, message = "bmId is required." });

            try
            {
                var (bytes, fileName) = await _svc.GenerateAsync(bmId, process, HttpContext.RequestAborted, format);
                return ReportFile(bytes, fileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }


    }
}
