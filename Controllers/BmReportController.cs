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
            [FromForm] string process, [FromForm] string format = "xlsx", bool metadata = false)
        {
            try
            {
                process = LegacyReportExport.NormalizeProcess(process);
                var (bytes, fileName) = await _svc.GenerateAsync(
                    bmId,
                    process, HttpContext.RequestAborted, format);

                var storedFile = await LegacyReportExport.SaveReportAsync(_config, true, process,
                    fileName, bytes, HttpContext.RequestAborted);

                if (metadata)
                    return Json(new { success = true, data = new { fileName,
                        downloadUrl = $"/BmReport?handler=Download&process={process}&fileName={Uri.EscapeDataString(storedFile)}" } });

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

        [HttpGet, WorkflowHandler("History")]
        public IActionResult History(int bmId, string process = "")
            => ReportHistory(_config, true, process, bmId, $"TE_{bmId}");

        [HttpGet, WorkflowHandler("Download")]
        public IActionResult Download(string fileName, string process = "", bool inline = false)
        {
            string path;
            try { path = LegacyReportExport.ReportPath(_config, true, process, fileName); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            if (!System.IO.File.Exists(path)) return NotFound("Report file not found.");
            var type = LegacyReportExport.ContentType(Path.GetExtension(fileName).TrimStart('.'));
            return inline && type == "application/pdf" ? PhysicalFile(path, type)
                : PhysicalFile(path, type, LegacyReportExport.DownloadName(fileName));
        }
    }
}
