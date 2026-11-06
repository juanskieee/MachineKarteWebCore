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

    protected FileContentResult ReportFile(byte[] bytes, string fileName)
    {
        Response.Headers.CacheControl = "no-store";
        return File(bytes, LegacyReportExport.ContentType(Path.GetExtension(fileName).TrimStart('.')), fileName);
    }
}
