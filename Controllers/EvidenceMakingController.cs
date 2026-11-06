// Converted from Form4.vb — E-Karte System
// ASP.NET Core workflow controller

using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
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
                if (result.Success) return ReportFile(result.Bytes, result.FileName);
                return new JsonResult(new AjaxResult<object>
                {
                    Success = result.Success,
                    Message = result.Message,
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


    }
}
