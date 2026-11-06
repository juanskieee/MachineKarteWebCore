using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers
{
    public class BmReportController : WorkflowController
    {
        private readonly BmReportService _svc;
        private readonly ILogger<BmReportController> _log;
        private readonly IConfiguration _config;

        public BmReportController(
            BmReportService svc,
            ILogger<BmReportController> log, IConfiguration config)
        {
            _svc = svc;
            _log = log;
            _config = config;
        }

        [HttpGet, WorkflowHandler]
        public IActionResult Index(int bmId = 0, string process = "")
        {
            if (bmId <= 0)
                return BadRequest("bmId is required.");

            return WorkflowView(new ReportWorkflowViewModel(bmId, process));
        }

        [HttpPost, WorkflowHandler("DownloadReport")]
        public async Task<IActionResult> DownloadReportAsync(
            [FromForm] int bmId,
            [FromForm] string process, [FromForm] string format = "xlsx")
        {
            try
            {
                process = LegacyReportExport.NormalizeProcess(process);
                var (bytes, fileName) = await _svc.GenerateAsync(
                    bmId,
                    process, HttpContext.RequestAborted, format);

                await LegacyReportExport.SaveReportAsync(_config, true, process,
                    fileName, bytes, HttpContext.RequestAborted);

                return File(
                    bytes,
                    LegacyReportExport.ContentType(format),
                    fileName);
            }
            catch (InvalidOperationException ex)
            {
                _log.LogWarning(
                    ex,
                    "BmReport: record not found bmId={BmId}",
                    bmId);

                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                _log.LogError(
                    ex,
                    "BmReport: generation failed bmId={BmId}",
                    bmId);

                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = "Report generation failed. Check server logs."
                });
            }
        }

        [HttpGet, WorkflowHandler("CheckRecord")]
        public async Task<IActionResult> CheckRecordAsync(int bmId)
        {
            try
            {
                var (_, _) = await _svc.GenerateAsync(bmId, "TE", HttpContext.RequestAborted);

                return new JsonResult(new AjaxResult<object>
                {
                    Success = true,
                    Message = "OK"
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }
    }
}
