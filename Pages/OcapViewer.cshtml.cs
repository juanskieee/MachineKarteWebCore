using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Pages
{
    public class OcapViewerModel : WorkflowPageModel
    {
        private readonly OcapViewerService _service;
        private readonly ILogger<OcapViewerModel> _logger;

        [BindProperty(SupportsGet = true)]
        public int BmId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string BmProcess { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string BmCaseId { get; set; } = string.Empty;

        public OcapViewerModel(
            OcapViewerService service,
            ILogger<OcapViewerModel> logger)
        {
            _service = service;
            _logger = logger;
        }

        public void OnGet()
        {
        }

        public IActionResult OnGetLoadOcapAsync(
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

                var data = _service.Load(req);

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