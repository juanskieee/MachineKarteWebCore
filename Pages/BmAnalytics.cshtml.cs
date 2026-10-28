using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Pages;

public class BmAnalyticsModel : PageModel
{
    private readonly BmAnalyticsService _svc;
    private readonly IConfiguration _cfg;

    public BmAnalyticsModel(
        BmAnalyticsService svc,
        IConfiguration cfg)
    {
        _svc = svc;
        _cfg = cfg;
    }

    public string AppVersion => _cfg["AppVersion"] ?? "1.0";

    // -----------------------------------------------------------------------
    // GET /BmAnalytics
    // -----------------------------------------------------------------------
    public IActionResult OnGet()
    {
        return Page();
    }

    // -----------------------------------------------------------------------
    // POST /BmAnalytics?handler=LoadGrid
    // -----------------------------------------------------------------------
    public async Task<IActionResult> OnPostLoadGridAsync(
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
    public async Task<IActionResult> OnGetCellMachinesAsync(
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
                Mode = mode
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