using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages;

public sealed class PmReportModel : WorkflowPageModel
{
    private readonly PmReportService _service;
    private readonly ILogger<PmReportModel> _logger;

    public PmReportModel(PmReportService service, ILogger<PmReportModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string Process { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public int BmId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string TimeOfRequest { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string MachineNo { get; set; } = string.Empty;

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnGetGenerateAsync(
        string process,
        int bmId,
        string timeOfRequest = "",
        string machineNo = "")
    {
        var result = await _service.GenerateAsync(new PmReportRequest
        {
            Process = process,
            BmId = bmId,
            TimeOfRequest = timeOfRequest,
            MachineNo = machineNo
        });

        return new JsonResult(new AjaxResult<PmReportResult>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result
        });
    }

    public IActionResult OnGetDownload(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(fileName) ||
            !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
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
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Path.GetFileName(fileName));
    }
}
