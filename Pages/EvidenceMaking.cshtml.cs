// Pages/EvidenceMaking.cshtml.cs
// Converted from Form4.vb — E-Karte System
// C# ASP.NET Core Razor Pages

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services.Reporting;

namespace MachineKarte.Web.Pages
{
    public class EvidenceMakingModel : PageModel
    {
        private readonly EvidenceMakingService _svc;
        private readonly ILogger<EvidenceMakingModel> _logger;

        public EvidenceMakingModel(
            EvidenceMakingService svc,
            ILogger<EvidenceMakingModel> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        // ── GET ──────────────────────────────────────────────────────
        // Supports being opened directly or with query-string pre-fill:
        //   /EvidenceMaking?process=DC&bmId=1234
        // Mirrors Form4 being called from Form1 with EMakingPrint args.

        [BindProperty(SupportsGet = true)]
        public string? PresetProcess { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? PresetBmId { get; set; }

        public void OnGet() { }

        // ── POST: Generate ───────────────────────────────────────────
        // Mirrors EMakingPrint(ProcessCase, bmid)
        // POST /EvidenceMaking?handler=Generate
        // Body: { process: "DC", bmId: 1234 }

        public async Task<IActionResult> OnPostGenerateAsync(
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
                var result = await _svc.GenerateEvidenceAsync(request);
                return new JsonResult(new AjaxResult<EvidenceGenerateResult>
                {
                    Success = result.Success,
                    Message = result.Message,
                    Data = result
                });
            }
            catch (Exception ex)
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

        public IActionResult OnGetDownload(string fileName)
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

            return PhysicalFile(filePath, contentType, fileName);
        }
    }
}