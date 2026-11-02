using System;
using System.IO;
using System.Threading.Tasks;
using MachineKarte.Web.Services.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Controllers
{
    /// <summary>
    /// Form10 → TeEvidenceMaking.cshtml
    ///
    /// Monitoring opens the TE evidence partial from a TE card's E action.
    /// The existing Generate and Download handlers supply the workbook to that
    /// dialog, matching Form10.EMakingPrint without standalone navigation.
    /// </summary>
    public class TeEvidenceMakingController : WorkflowController
    {
        private readonly TeEvidenceMakingService _svc;
        private readonly string _outputRoot;

        public TeEvidenceMakingController(TeEvidenceMakingService svc, IConfiguration cfg)
        {
            _svc = svc;
            _outputRoot = cfg["EvidenceMaking:OutputRoot"] ?? "TempData/GeneratedPdf";
        }

        [HttpGet, WorkflowHandler]
        public IActionResult Index() => WorkflowView();

        // ─── AJAX: generate TE evidence workbook ──────────────────────────────
        // GET /TeEvidenceMaking?handler=Generate&bmId=12345

        [HttpGet, WorkflowHandler("Generate")]
        public async Task<IActionResult> GenerateAsync(string bmId)
        {
            if (string.IsNullOrWhiteSpace(bmId))
                return new JsonResult(new { success = false, message = "bmId is required." });

            try
            {
                string fileUrl = await _svc.GenerateAsync(bmId);
                return new JsonResult(new { success = true, url = fileUrl });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // ─── Stream generated file for download ───────────────────────────────
        // GET /TeEvidenceMaking?handler=Download&bmId=12345

        [HttpGet, WorkflowHandler("Download")]
        public IActionResult Download(string bmId)
        {
            if (string.IsNullOrWhiteSpace(bmId))
                return BadRequest("bmId required.");

            string fileName = $"TE_Evidence_{bmId}.xlsx";
            string path = Path.Combine(_outputRoot, fileName);

            if (!System.IO.File.Exists(path))
                return NotFound($"Evidence file for BM {bmId} has not been generated yet.");

            var stream = System.IO.File.OpenRead(path);
            return File(stream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }
}
