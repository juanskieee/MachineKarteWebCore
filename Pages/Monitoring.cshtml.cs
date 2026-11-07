using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using System.Globalization;

namespace MachineKarte.Web.Pages;

public class MonitoringModel : PageModel
{
    private readonly MonitoringService _service;
    private readonly ILogger<MonitoringModel> _logger;

    public MonitoringModel(MonitoringService service, ILogger<MonitoringModel> logger)
        => (_service, _logger) = (service, logger);

    public string DefaultDateFrom => DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
    public string DefaultDateTo => DateTime.Today.ToString("yyyy-MM-dd");

    public IActionResult OnGetProcessList(string userType) =>
        new JsonResult(new AjaxResult<List<string>> { Data = _service.GetProcessList(userType) });

    public IActionResult OnGetFloorList() =>
        new JsonResult(new AjaxResult<List<string>> { Data = _service.GetFloorList() });

    public async Task<IActionResult> OnGetMachineListAsync(string userType, string process, string floor)
    {
        if (!MonitoringService.IsSpecificFloor(floor)) return InvalidLocation();

        var started = Stopwatch.GetTimestamp();
        var machines = await _service.GetMachineListAsync(userType, process, floor, HttpContext.RequestAborted);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Response.Headers["Server-Timing"] = "machine-list;dur=" + elapsed.ToString("F1", CultureInfo.InvariantCulture);
        _logger.LogInformation("Machine list {UserType}/{Process}/{Floor}: {ElapsedMs:F1}ms.", userType, process, floor, elapsed);
        return new JsonResult(new AjaxResult<MachineListResult>
        {
            Data = new MachineListResult { Machines = machines }
        });
    }

    public async Task<IActionResult> OnPostSearchAsync([FromBody] MonitoringFilter filter)
    {
        if (filter is null) return Failure("Invalid request.");
        if (!MonitoringService.IsSpecificFloor(filter.Floor)) return InvalidLocation();
        if (string.IsNullOrWhiteSpace(filter.Machine)) return Failure("Please select a machine number.");
        if (string.IsNullOrWhiteSpace(filter.Process) || filter.Process == "-Select-")
            return Failure("Please select a process.");

        try
        {
            filter.Page = Math.Max(1, filter.Page);
            var result = await _service.SearchDashboardAsync(filter, HttpContext.RequestAborted);
            return new JsonResult(new AjaxResult<MonitoringDashboardResult> { Data = result });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(ex, "Search failed");
            return Failure("Search failed: " + ex.Message);
        }
    }

    public async Task<IActionResult> OnGetMachineCardAsync(
        string userType, string process, string floor, string machineNo,
        string machineType, int actionLine, string dateFrom, string dateTo)
    {
        if (!MonitoringService.IsSpecificFloor(floor)) return InvalidLocation();

        try
        {
            var filter = new MonitoringFilter
            {
                UserType = userType,
                Process = process,
                Floor = floor,
                DateFrom = dateFrom,
                DateTo = dateTo
            };
            var result = await _service.GetMachineCardAsync(
                filter, machineNo, machineType, actionLine, HttpContext.RequestAborted);
            return new JsonResult(new AjaxResult<MachineCardResult> { Data = result });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(ex, "Machine card load failed for {Machine}", machineNo);
            return Failure(ex.Message);
        }
    }

    // Optional tracking starts after page load and cannot hold a database connection indefinitely.
    public async Task<IActionResult> OnPostTrackSessionAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _service.TrackSessionAsync(
                Environment.MachineName,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                typeof(MonitoringModel).Assembly.GetName().Version?.ToString() ?? "Unknown",
                timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogDebug("PC status tracking timed out or the request was canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("PC status tracking skipped: {Reason}", ex.Message);
        }
        return new NoContentResult();
    }

    private JsonResult InvalidLocation() => Failure("Please select a specific location.");

    private static JsonResult Failure(string message) =>
        new(new AjaxResult<object> { Success = false, Message = message });
}
