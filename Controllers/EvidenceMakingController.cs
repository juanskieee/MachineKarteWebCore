// Converted from Form4.vb — E-Karte System
// ASP.NET Core workflow controller

using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services.Reporting;

namespace MachineKarte.Web.Controllers
{
    public class EvidenceMakingController : WorkflowController
    {
        private readonly EvidenceMakingService _svc;
        private readonly ILogger<EvidenceMakingController> _logger;

        public EvidenceMakingController(
            EvidenceMakingService svc,
            ILogger<EvidenceMakingController> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        // ── GET ──────────────────────────────────────────────────────
        // Supports being opened directly or with query-string pre-fill:
        //   /EvidenceMaking?process=DC&bmId=1234
        // Mirrors Form4 being called from Form1 with EMakingPrint args.

        [HttpGet, WorkflowHandler]
        public IActionResult Index() => WorkflowView();

        // ── POST: Generate ───────────────────────────────────────────
        // Mirrors EMakingPrint(ProcessCase, bmid)
        // POST /EvidenceMaking?handler=Generate
        // Body: { process: "DC", bmId: 1234 }

        [HttpPost, WorkflowHandler("Generate")]
        public async Task<IActionResult> GenerateAsync(
            [FromBody] EvidenceGenerateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Process))
                return new JsonResult(new AjaxResult<EvidenceGenerateResult>
                {
                    Success = false,
                    Message = "Please select a process."
                });

            if (request.BmId <= 0)
                return new JsonResult(new AjaxResult<EvidenceGenerateResult>
                {
                    Success = false,
                    Message = "Please enter a valid BM ID."
                });

            if (ProcessTemplateMap.Get(request.Process) is null)
                return new JsonResult(new AjaxResult<EvidenceGenerateResult>
                {
                    Success = false,
                    Message = $"Process '{request.Process}' is not configured."
                });

            try
            {
                var result = await _svc.GenerateEvidenceAsync(request, HttpContext.RequestAborted);
                return new JsonResult(new AjaxResult<EvidenceGenerateResult>
                {
                    Success = result.Success,
                    Message = result.Message,
                    Data = result
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogError(ex, "Evidence generation failed for BM {BmId}", request.BmId);
                return new JsonResult(new AjaxResult<EvidenceGenerateResult>
                {
                    Success = false,
                    Message = "Evidence generation failed: " + ex.Message
                });
            }
        }

        // ── GET: Download generated file ─────────────────────────────
        // Streams the generated xlsx/pdf back to the browser.
        // Mirrors: wb.Save(localFilePath) + Process.Start("iexplore.exe", path)
        // GET /EvidenceMaking?handler=Download&fileName=DCEvidence_123.xlsx

        [HttpGet, WorkflowHandler("Download")]
        public IActionResult Download(string fileName, bool inline = false)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.Contains("..") || Path.IsPathRooted(fileName))
                return BadRequest("Invalid file name.");

            string filePath = Path.Combine(
                HttpContext.RequestServices
                    .GetRequiredService<IWebHostEnvironment>().ContentRootPath,
                "TempData", "GeneratedPdf",
                Path.GetFileName(fileName));   // GetFileName strips path traversal

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found. Please generate it first.");

            string contentType = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return inline && contentType == "application/pdf"
                ? PhysicalFile(filePath, contentType)
                : PhysicalFile(filePath, contentType, fileName);
        }
    }
}
