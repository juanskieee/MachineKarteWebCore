using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers;

[Route("[controller]")]
[AutoValidateAntiforgeryToken]
public abstract class WorkflowController : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.RouteValues["action"] != "Index" || Request.Query["dialog"] == "1")
            return;

        var query = Request.Query
            .Where(item => item.Key != "workflow" && item.Key != "dialog")
            .SelectMany(item => item.Value.Select(value =>
                new KeyValuePair<string, string?>(item.Key, value)))
            .Prepend(new KeyValuePair<string, string?>("workflow", RouteData.Values["controller"]?.ToString()));
        context.Result = Redirect("/Monitoring" + QueryString.Create(query));
    }

    protected IActionResult WorkflowView(object? model = null)
        => PartialView($"/Pages/Shared/Workflows/_{RouteData.Values["controller"]}.cshtml", model);

    protected IActionResult ReportHistory(IConfiguration config, bool te, string process, int bmId, string stem)
    {
        if (bmId <= 0) return BadRequest(new { message = "A valid BM ID is required." });
        try
        {
            process = LegacyReportExport.NormalizeProcess(process);
            var files = LegacyReportExport.ListReports(config, te, process, stem).Select(file => new
            {
                fileName = stem + Path.GetExtension(file.FileName),
                file.GeneratedAtUtc,
                file.SizeBytes,
                downloadUrl = "/" + RouteData.Values["controller"] + QueryString.Create(new Dictionary<string, string?>
                {
                    ["handler"] = "Download", ["process"] = process, ["bmId"] = bmId.ToString(),
                    ["format"] = Path.GetExtension(file.FileName).TrimStart('.'), ["fileName"] = file.FileName
                })
            });
            Response.Headers.CacheControl = "no-store";
            return Json(new { success = true, data = files });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<WorkflowController>>()
                .LogWarning(ex, "Generated report history unavailable for {Process}/{BmId}", process, bmId);
            return StatusCode(503, new { message = "Saved files could not be loaded. Check report storage and try Refresh." });
        }
    }
}
