using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MachineKarte.Web.Pages;

// These routes keep their existing AJAX/download handlers. Their UI is a partial
// hosted by Monitoring; bookmarked URLs reopen the same workflow there.
public abstract class WorkflowPageModel : PageModel
{
    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var isView = HttpMethods.IsGet(Request.Method)
            && string.IsNullOrEmpty(context.HandlerMethod?.Name);
        var workflow = GetType().Name.Replace("Model", "", StringComparison.Ordinal);

        if (isView && Request.Query["dialog"] != "1")
        {
            var query = Request.Query
                .Where(item => item.Key != "workflow" && item.Key != "dialog")
                .SelectMany(item => item.Value.Select(value =>
                    new KeyValuePair<string, string?>(item.Key, value)))
                .Prepend(new KeyValuePair<string, string?>("workflow", workflow));
            context.Result = Redirect("/Monitoring" + QueryString.Create(query));
            return;
        }

        var executed = await next();
        if (isView && executed.Exception is null && executed.Result is PageResult)
            executed.Result = Partial($"Workflows/_{workflow}", this);
    }
}
