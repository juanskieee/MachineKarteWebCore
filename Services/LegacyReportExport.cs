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

    public static string ResolveEvidenceImage(string relativePath, string evidenceRoot, string imageRoot)
    {
        var relative = relativePath.Replace("~", "").Replace('\\', '/').TrimStart('/');
        if (!relative.Split('/').Contains("..") && !relative.Contains(':'))
        {
            var imageRelative = relative.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ? relative[7..] : relative;
            foreach (var candidate in new[] { Path.Combine(evidenceRoot, relative), Path.Combine(imageRoot, imageRelative) })
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return Path.Combine(imageRoot, "noimage.png");
    }
}
