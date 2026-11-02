using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers
{
    public class RawDataController : WorkflowController
    {
        private readonly RawDataService _service;
        private readonly ILogger<RawDataController> _logger;

        public RawDataController(
            RawDataService service,
            ILogger<RawDataController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet, WorkflowHandler]
        public IActionResult Index(
            string process = "", string location = "", string machine = "",
            string dateFrom = "", string dateTo = "")
            => WorkflowView(new RawDataViewModel(process, location, machine, dateFrom, dateTo));

        [HttpGet, WorkflowHandler("LoadData")]
        public async Task<IActionResult> LoadDataAsync(
            string process,
            string location,
            string machine,
            string dateFrom,
            string dateTo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(process) ||
                    string.IsNullOrWhiteSpace(location) ||
                    string.IsNullOrWhiteSpace(dateFrom) ||
                    string.IsNullOrWhiteSpace(dateTo))
                {
                    return new JsonResult(new AjaxResult<object>
                    {
                        Success = false,
                        Message = "Required parameters missing (process, location, dateFrom, dateTo)."
                    });
                }

                var req = new RawDataRequest
                {
                    Process = process,
                    Location = location,
                    Machine = machine,
                    DateFrom = dateFrom,
                    DateTo = dateTo
                };

                var data = await _service.LoadAsync(req, HttpContext.RequestAborted);

                return new JsonResult(new AjaxResult<RawDataResult>
                {
                    Success = true,
                    Data = data
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid parameter in RawData request.");

                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error loading raw data.");

                return new JsonResult(new AjaxResult<object>
                {
                    Success = false,
                    Message = "An unexpected error occurred while loading data."
                });
            }
        }
    }
}
