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

    // Only configured, trusted roots are searched. Never use a database-supplied
    // absolute/UNC path directly, and never allow parent-directory traversal.
    public static string ResolveEvidenceImage(string relativePath, string evidenceRoot, string imageRoot,
        IEnumerable<string>? legacyUploadRoots = null)
    {
        var path = (relativePath ?? string.Empty).Trim().Trim('"').Replace('\\\\', '/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            return Path.Combine(imageRoot, "noimage.png");

        // Legacy records can store full UNC paths. Extract only their path below
        // the Upload directory and resolve it under an explicitly trusted root.
        var uploadIndex = Array.FindLastIndex(segments, segment =>
            segment.Equals("Upload", StringComparison.OrdinalIgnoreCase));
        var relative = uploadIndex >= 0
            ? segments.Skip(uploadIndex + 1).ToArray()
            : segments;
        if (relative.Length == 0 || relative.Any(segment => segment.Contains(':')))
            return Path.Combine(imageRoot, "noimage.png");

        var localRelative = relative[0].Equals("images", StringComparison.OrdinalIgnoreCase)
            ? relative.Skip(1).ToArray() : relative;
        var roots = new[] { evidenceRoot }.Concat(legacyUploadRoots ?? Array.Empty<string>());
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var candidate = Path.Combine(new[] { root }.Concat(relative).ToArray());
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        var staticCandidate = Path.Combine(new[] { imageRoot }.Concat(localRelative).ToArray());
        return File.Exists(staticCandidate) ? Path.GetFullPath(staticCandidate) : Path.Combine(imageRoot, "noimage.png");
    }
}
