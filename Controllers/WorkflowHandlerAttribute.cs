using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;

namespace MachineKarte.Web.Controllers;

// Preserve the existing query-string contract while MVC owns the workflow routes.
[AttributeUsage(AttributeTargets.Method)]
public sealed class WorkflowHandlerAttribute(string handler = "") : ActionMethodSelectorAttribute
{
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
        => string.Equals(routeContext.HttpContext.Request.Query["handler"].ToString(),
            handler, StringComparison.OrdinalIgnoreCase);
}
