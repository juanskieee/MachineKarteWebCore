using GemBox.Spreadsheet;

namespace MachineKarte.Web.Services;

public static class LegacyReportExport
{
    static LegacyReportExport() => SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");

    public static string NormalizeFormat(string format) => format?.ToLowerInvariant() switch
    {
        "xlsx" => "xlsx",
        "pdf" => "pdf",
        _ => throw new ArgumentException("Choose Excel or PDF output.")
    };

    public static string ContentType(string format) => NormalizeFormat(format) == "pdf"
        ? "application/pdf"
        : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static ExcelFile Load(string path) => ExcelFile.Load(path);

    public static byte[] Save(ExcelFile workbook, string format)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream, NormalizeFormat(format) == "pdf" ? SaveOptions.PdfDefault : SaveOptions.XlsxDefault);
        return stream.ToArray();
    }

    public static string ImageRoot(IWebHostEnvironment environment, bool te) =>
        Path.Combine(environment.ContentRootPath, "Assets", "images", te ? "TEeMaintenanceSystems_images" : "eMaintenanceSystems_images");

    // Form4:  \\\\PathServer\\eMaintenanceSystem + BMEvidence_ImgFile
    // Form10: \\\\PathServer\\TEeMaintenanceSystem + BMEvidence_ImgFile.Replace("~", "")
    // The network share and its directories already exist; there is no web upload path.
    public static string LegacyEvidenceRoot(IConfiguration config, bool te)
    {
        var server = config["NetworkShare:Server"]?.Trim();
        if (string.IsNullOrEmpty(server) || server is "." or ".." ||
            server.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            throw new InvalidOperationException("NetworkShare:Server must be the legacy file server hostname or IP.");
        return @"\\" + server + (te ? @"\TEeMaintenanceSystem" : @"\eMaintenanceSystem");
    }

    // Database evidence paths are share-relative (usually \\Upload\\...). Reject
    // foreign absolute paths and traversal rather than probing arbitrary locations.
    // An absent file uses the original Form4/Form10 noimage.png mark.
    public static string ResolveEvidenceImage(string storedPath, string legacySystemRoot, string imageRoot, bool te = false)
    {
        var placeholder = Path.Combine(imageRoot, "noimage.png");
        if (string.IsNullOrEmpty(storedPath)) return placeholder;

        var path = (te ? storedPath.Replace("~", "") : storedPath).Replace('/', '\\');
        if (!path.StartsWith('\\') || path.StartsWith(@"\\") || path.Contains(':'))
            return placeholder;

        var parts = path.Substring(1).Split('\\');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            return placeholder;

        var candidate = Path.Combine(new[] { legacySystemRoot }.Concat(parts).ToArray());
        // Windows UNC paths are normalized and bounded by the original share.
        if (OperatingSystem.IsWindows())
        {
            var root = Path.GetFullPath(legacySystemRoot).TrimEnd('\\') + "\\";
            candidate = Path.GetFullPath(candidate);
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return placeholder;
        }
        return File.Exists(candidate) ? candidate : placeholder;
    }
}
