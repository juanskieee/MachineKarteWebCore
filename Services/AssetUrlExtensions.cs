using Microsoft.AspNetCore.Mvc;

namespace MachineKarte.Web.Services;

public static class AssetUrlExtensions
{
    public static string VersionedAsset(this IUrlHelper url, string path)
    {
        // Assets live outside wwwroot, where append-version cannot find them.
        var environment = url.ActionContext.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var modified = environment.ContentRootFileProvider.GetFileInfo(path).LastModified.UtcDateTime.Ticks;
        return url.Content("~/" + path) + "?v=" + modified;
    }
}
