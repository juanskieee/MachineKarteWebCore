using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers;

public class BmAnalyticsController : WorkflowController
{
    private readonly BmAnalyticsService _svc;
    public BmAnalyticsController(BmAnalyticsService svc) => _svc = svc;

    // -----------------------------------------------------------------------
    // GET /BmAnalytics
    // -----------------------------------------------------------------------
    [HttpGet, WorkflowHandler]
    public IActionResult Index(string? legacyForm = null)
        => WorkflowView(new BmAnalyticsViewModel(legacyForm == "12" ? "12" : "8"));

    // -----------------------------------------------------------------------
    // POST /BmAnalytics?handler=LoadGrid
    // -----------------------------------------------------------------------
    [HttpPost, WorkflowHandler("LoadGrid")]
    public async Task<IActionResult> LoadGridAsync(
        [FromBody] BmAnalyticsRequest req)
    {
        try
        {
            var result = await _svc.BuildGridAsync(req);

            return new JsonResult(
                AjaxResult.Ok(result)
            );
        }
        catch (Exception ex)
        {
            return new JsonResult(
                AjaxResult.Fail(ex.Message)
            );
        }
    }

    // -----------------------------------------------------------------------
    // GET /BmAnalytics?handler=CellMachines
    // -----------------------------------------------------------------------
    [HttpGet, WorkflowHandler("CellMachines")]
    public async Task<IActionResult> CellMachinesAsync(
        string key,
        string dateFrom,
        string dateTo,
        string mode)
    {
        try
        {
            var req = new BmAnalyticsRequest
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                Mode = mode,
                LegacyForm = Request.Query["legacyForm"] == "12" ? "12" : "8"
            };

            var result = await _svc.BuildGridAsync(req);

            if (result.Cells.TryGetValue(key, out var cell))
            {
                return new JsonResult(
                    AjaxResult.Ok(cell.Mcnos)
                );
            }

            return new JsonResult(
                AjaxResult.Ok(new List<string>())
            );
        }
        catch (Exception ex)
        {
            return new JsonResult(
                AjaxResult.Fail(ex.Message)
            );
        }
    }
}
