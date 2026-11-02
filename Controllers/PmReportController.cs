using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Controllers;

public sealed class PmReportController : WorkflowController
{
    private readonly PmReportService _service;
    private readonly ILogger<PmReportController> _logger;

    public PmReportController(PmReportService service, ILogger<PmReportController> logger)
    {
        _service = service;
        _logger = logger;
    }

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

        return new JsonResult(new AjaxResult<PmReportResult>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result
        });
    }

    [HttpGet, WorkflowHandler("Download")]
    public IActionResult Download(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(fileName) ||
            !(fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            return BadRequest("Invalid report file name.");

        var path = Path.Combine(
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
            "TempData",
            "GeneratedPdf",
            Path.GetFileName(fileName));

        if (!System.IO.File.Exists(path))
            return NotFound("Report file not found.");

        return PhysicalFile(
            path,
            LegacyReportExport.ContentType(Path.GetExtension(fileName).TrimStart('.')),
            Path.GetFileName(fileName));
    }
}
