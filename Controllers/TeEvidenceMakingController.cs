using System;
using System.IO;
using System.Threading.Tasks;
using MachineKarte.Web.Services.Reporting;
using MachineKarte.Web.Services;
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
        private readonly IConfiguration _config;

        public TeEvidenceMakingController(TeEvidenceMakingService svc, IConfiguration cfg)
        {
            _svc = svc;
            _config = cfg;
        }

        [HttpGet, WorkflowHandler]
        public IActionResult Index() => WorkflowView();

        [HttpGet, WorkflowHandler("History")]
        public IActionResult History(int bmId, string process = "")
            => ReportHistory(_config, true, process, bmId, $"TE_Evidence_{bmId}");

        // ─── AJAX: generate TE evidence workbook ──────────────────────────────
        // GET /TeEvidenceMaking?handler=Generate&bmId=12345

        [HttpGet, WorkflowHandler("Generate")]
        public async Task<IActionResult> GenerateAsync(string bmId, string format = "xlsx", string process = "")
        {
            if (string.IsNullOrWhiteSpace(bmId))
                return new JsonResult(new { success = false, message = "bmId is required." });

            try
            {
                string fileUrl = await _svc.GenerateAsync(bmId, process, HttpContext.RequestAborted, format);
                return new JsonResult(new { success = true, url = fileUrl });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // ─── Stream generated file for download ───────────────────────────────
        // GET /TeEvidenceMaking?handler=Download&bmId=12345

        [HttpGet, WorkflowHandler("Download")]
        public IActionResult Download(string bmId, string format = "xlsx", bool inline = false, string process = "", string? fileName = null)
        {
            if (string.IsNullOrWhiteSpace(bmId))
                return BadRequest("bmId required.");

            if (!int.TryParse(bmId, out var id) || id <= 0 || format is not ("xlsx" or "pdf"))
                return BadRequest("Invalid evidence file request.");
            fileName ??= $"TE_Evidence_{id}.{format}";
            if (!LegacyReportExport.IsReportFile(fileName, $"TE_Evidence_{id}", format))
                return BadRequest("Invalid evidence file request.");
            string path;
            try { path = LegacyReportExport.ReportPath(_config, true, process, fileName); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }

            if (!System.IO.File.Exists(path))
                return NotFound($"Evidence file for BM {bmId} has not been generated yet.");

            var stream = System.IO.File.OpenRead(path);
            var contentType = LegacyReportExport.ContentType(format);
            // Preserve attachment downloads; only inline PDF previews omit the filename.
            return inline && format == "pdf"
                ? File(stream, contentType)
                : File(stream, contentType, LegacyReportExport.DownloadName(fileName));
        }
    }
}
