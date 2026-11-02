using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers
{
    public class OcapViewerController : WorkflowController
    {
        private readonly OcapViewerService _service;
        private readonly ILogger<OcapViewerController> _logger;

        public OcapViewerController(
            OcapViewerService service,
            ILogger<OcapViewerController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet, WorkflowHandler]
        public IActionResult Index(int bmId = 0, string bmProcess = "", string bmCaseId = "")
            => WorkflowView(new OcapViewerViewModel(bmId, bmProcess, bmCaseId));

        [HttpGet, WorkflowHandler("LoadOcap")]
        public async Task<IActionResult> LoadOcapAsync(
            int bmId,
            string bmProcess,
            string bmCaseId)
        {
            try
            {
                if (bmId <= 0 ||
                    string.IsNullOrWhiteSpace(bmProcess) ||
                    string.IsNullOrWhiteSpace(bmCaseId))
                {
                    return new JsonResult(new AjaxResult<object>
                    {
                        Success = false,
                        Message = "Required parameters missing (bmId, bmProcess, bmCaseId)."
                    });
                }

                var req = new OcapViewerRequest
                {
                    BmId = bmId,
                    BmProcess = bmProcess,
                    BmCaseId = bmCaseId
                };

                var data = await _service.LoadAsync(req, HttpContext.RequestAborted);

                return new JsonResult(new AjaxResult<OcapViewerResult>
                {
                    Success = true,
                    Data = data
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid parameter in OcapViewer request.");

                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading OCAP data.");

                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = "An unexpected error occurred while loading OCAP data."
                });
            }
        }
    }
}
