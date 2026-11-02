using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers
{
    public class BmReportController : WorkflowController
    {
        private readonly BmReportService _svc;
        private readonly ILogger<BmReportController> _log;

        public BmReportController(
            BmReportService svc,
            ILogger<BmReportController> log)
        {
            _svc = svc;
            _log = log;
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
            [FromForm] string process)
        {
            try
            {
                var (bytes, fileName) = await _svc.GenerateAsync(
                    bmId,
                    process ?? "TE");

                return File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
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
            catch (Exception ex)
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
                var (_, _) = await _svc.GenerateAsync(bmId, "TE");

                return new JsonResult(new AjaxResult<object>
                {
                    Success = true,
                    Message = "OK"
                });
            }
            catch (Exception ex)
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
