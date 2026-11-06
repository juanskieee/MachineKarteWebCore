using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Controllers;

public sealed class PmReportController : WorkflowController
{
    private readonly PmReportService _service;
    private readonly ILogger<PmReportController> _logger;
    private readonly IConfiguration _config;

    public PmReportController(PmReportService service, ILogger<PmReportController> logger, IConfiguration config)
    {
        _service = service;
        _logger = logger;
        _config = config;
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
    public IActionResult Download(string fileName, bool inline = false, string process = "")
    {
        string path;
        try { path = LegacyReportExport.ReportPath(_config, false, process, fileName); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }

        if (!System.IO.File.Exists(path))
            return NotFound("Report file not found.");

        var contentType = LegacyReportExport.ContentType(Path.GetExtension(fileName).TrimStart('.'));
        // A PDF iframe must not receive Content-Disposition: attachment.
        return inline && contentType == "application/pdf"
            ? PhysicalFile(path, contentType)
            : PhysicalFile(path, contentType, LegacyReportExport.DownloadName(fileName));
    }

    [HttpGet, WorkflowHandler("History")]
    public IActionResult History(int bmId, string process = "")
        => ReportHistory(_config, false, process, bmId, $"{process.Trim().ToUpperInvariant()}_{bmId}");
}
