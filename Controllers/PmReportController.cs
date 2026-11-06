using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Controllers;

public sealed class PmReportController : WorkflowController
{
    private readonly PmReportService _service;
    public PmReportController(PmReportService service) => _service = service;

    [HttpGet, WorkflowHandler]
    public IActionResult Index(int bmId = 0, string process = "")
        => WorkflowView(new ReportWorkflowViewModel(bmId, process));

    [HttpGet, WorkflowHandler("Generate")]
    public async Task<IActionResult> GenerateAsync(
        string process,
        int bmId,
        string timeOfRequest = "",
        string machineNo = "", string format = "xlsx")
    {
        var result = await _service.GenerateAsync(new PmReportRequest
        {
            Process = process,
            BmId = bmId,
            TimeOfRequest = timeOfRequest,
            MachineNo = machineNo,
            Format = format
        }, HttpContext.RequestAborted);

        if (result.Success) return ReportFile(result.Bytes, result.FileName);
        return new JsonResult(new AjaxResult<object>
        {
            Success = result.Success,
            Message = result.Message,
        });
    }


}
