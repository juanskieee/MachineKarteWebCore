using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Controllers;

public class AlertBoardController : WorkflowController
{
	private readonly AlertBoardService _service;
	private readonly ILogger<AlertBoardController> _logger;

	public AlertBoardController(AlertBoardService service, ILogger<AlertBoardController> logger)
	{
		_service = service;
		_logger = logger;
	}

	[HttpGet, WorkflowHandler]
	public IActionResult Index() => WorkflowView();

	[HttpPost, WorkflowHandler("LoadBoard")]
	public async Task<IActionResult> LoadBoardAsync([FromBody] AlertBoardRequest request)
	{
		try
		{
			var board = await _service.LoadBoardAsync(request, HttpContext.RequestAborted);
			return new JsonResult(new AjaxResult<AlertBoardData> { Data = board });
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
		{
			_logger.LogError(ex, "Alert Board load failed");
			return new JsonResult(new AjaxResult<AlertBoardData>
			{
				Success = false,
				Message = "Alert Board load failed: " + ex.Message
			});
		}
	}

	[HttpGet, WorkflowHandler("Drilldown")]
	public async Task<IActionResult> DrilldownAsync(
		string cellKey, string dateFrom, string dateTo, string user = "")
	{
		try
		{
			var board = await _service.LoadBoardAsync(new AlertBoardRequest
			{
				DateFrom = dateFrom,
				DateTo = dateTo,
				User = user
			}, HttpContext.RequestAborted);
			var drilldown = _service.GetDrilldown(board, cellKey);
			return new JsonResult(new AjaxResult<object>
			{
				Data = new { machines = drilldown.Machines, title = drilldown.Title }
			});
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
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
