using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Controllers
{
    public class MachineSummaryController : WorkflowController
    {
        private readonly MachineSummaryService _service;

        public MachineSummaryController(MachineSummaryService service) => _service = service;

        [HttpGet, WorkflowHandler]
        public IActionResult Index() => WorkflowView();

        [HttpGet, WorkflowHandler("Summary")]
        public async Task<IActionResult> SummaryAsync(
            string dateFrom, string dateTo, string userMode = "EEE")
        {
            try
            {
                var result = await _service.BuildSummaryAsync(new MachineSummaryQuery
                {
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    UserMode = userMode
                }, HttpContext.RequestAborted);

                return new JsonResult(AjaxResult.Ok(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }

        [HttpGet, WorkflowHandler("MachineList")]
        public async Task<IActionResult> MachineListAsync(
            string process, string location, string dateFrom, string dateTo,
            string userMode = "EEE")
        {
            try
            {
                var result = await _service.GetCellAsync(process, location, new MachineSummaryQuery
                {
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    UserMode = userMode
                }, HttpContext.RequestAborted);

                return new JsonResult(AjaxResult.Ok(result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new JsonResult(AjaxResult.Fail(ex.Message));
            }
        }

    }
}
