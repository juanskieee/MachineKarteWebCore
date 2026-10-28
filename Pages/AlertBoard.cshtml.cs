using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages;

public class AlertBoardModel : PageModel
{
	private readonly AlertBoardService _service;
	private readonly ILogger<AlertBoardModel> _logger;

	public AlertBoardModel(AlertBoardService service, ILogger<AlertBoardModel> logger)
	{
		_service = service;
		_logger = logger;
	}

	public IActionResult OnGet() => Page();

	public async Task<IActionResult> OnPostLoadBoardAsync([FromBody] AlertBoardRequest request)
	{
		try
		{
			var board = await _service.LoadBoardAsync(request);
			return new JsonResult(new AjaxResult<AlertBoardData> { Data = board });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Alert Board load failed");
			return new JsonResult(new AjaxResult<AlertBoardData>
			{
				Success = false,
				Message = "Alert Board load failed: " + ex.Message
			});
		}
	}

	public async Task<IActionResult> OnGetDrilldownAsync(
		string cellKey, string dateFrom, string dateTo, string user = "")
	{
		try
		{
			var board = await _service.LoadBoardAsync(new AlertBoardRequest
			{
				DateFrom = dateFrom,
				DateTo = dateTo,
				User = user
			});
			var drilldown = _service.GetDrilldown(board, cellKey);
			return new JsonResult(new AjaxResult<object>
			{
				Data = new { machines = drilldown.Machines, title = drilldown.Title }
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Alert Board drilldown failed for {CellKey}", cellKey);
			return new JsonResult(new AjaxResult<object>
			{
				Success = false,
				Message = "Alert Board drilldown failed: " + ex.Message
			});
		}
	}
}
