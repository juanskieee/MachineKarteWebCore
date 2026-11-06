using GemBox.Spreadsheet;

namespace MachineKarte.Web.Services;

public static class LegacyReportExport
{
    private static readonly Dictionary<string, (DateTime Modified, long Length, ExcelFile Workbook)> Templates = new(StringComparer.OrdinalIgnoreCase);
    static LegacyReportExport() => SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");

    public static string NormalizeProcess(string process)
    {
        var value = process?.Trim().ToUpperInvariant();
        return value is "DC" or "DB" or "WB" or "MP" or "PL" or "FT" or "FL" or "TP" or "SING" or "MEMS" or "EDS" or "ATC"
            ? value : throw new ArgumentException("A valid report process is required.");
    }

    public static string NormalizeFormat(string format) => format?.ToLowerInvariant() switch
    {
        "xlsx" => "xlsx",
        "pdf" => "pdf",
        _ => throw new ArgumentException("Choose Excel or PDF output.")
    };

    public static string ContentType(string format) => NormalizeFormat(format) == "pdf"
        ? "application/pdf"
        : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static ExcelFile Load(string path)
    {
        var file = new FileInfo(path);
        // ponytail: serialize template cloning; use per-template locks if export contention warrants it.
        lock (Templates)
        {
            if (!Templates.TryGetValue(file.FullName, out var template) ||
                template.Modified != file.LastWriteTimeUtc || template.Length != file.Length)
            {
                template = (file.LastWriteTimeUtc, file.Length, ExcelFile.Load(file.FullName));
                Templates[file.FullName] = template;
            }
            return template.Workbook.Clone();
        }
    }

    public static byte[] Save(ExcelFile workbook, string format)
    {
        using var stream = new MemoryStream();
        workbook.Save(stream, NormalizeFormat(format) == "pdf"
            ? new PdfSaveOptions { SelectionType = SelectionType.EntireFile }
            : SaveOptions.XlsxDefault);
        return stream.ToArray();
    }

    public static string ImageRoot(IWebHostEnvironment environment, bool te) =>
        Path.Combine(environment.ContentRootPath, "Assets", "images", te ? "TEeMaintenanceSystems_images" : "eMaintenanceSystems_images");

    public static string LegacyEvidenceRoot(IConfiguration config, bool te) =>
        LegacyServerRoot(config) + (te ? @"\TEeMaintenanceSystem" : @"\eMaintenanceSystem");

    public static string SharedEvidenceRoot(IConfiguration config) =>
        LegacyServerRoot(config) + @"\Upload\Evidence";

    private static string LegacyServerRoot(IConfiguration config)
    {
        var server = config["NetworkShare:Server"]?.Trim();
        if (string.IsNullOrEmpty(server) || server is "." or ".." ||
            server.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            throw new InvalidOperationException("NetworkShare:Server must be the legacy file server hostname or IP.");
        return @"\\" + server;
    }

    // Read existing uploads under the workflow's own share or the shared archive.
    // Absolute paths must name one of those roots; never search by filename alone.
    public static string ResolveEvidenceImage(string storedPath, string legacySystemRoot, string imageRoot,
        bool te = false, string? sharedEvidenceRoot = null)
    {
        var placeholder = Path.Combine(imageRoot, "noimage.png");
        if (string.IsNullOrEmpty(storedPath)) return placeholder;

        var path = (te ? storedPath.Replace("~", "") : storedPath).Replace('/', '\\');
        try
        {
            string? ExistingImage(string root, string relative)
            {
                var parts = relative.Split('\\');
                if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains(':')))
                    return null;
                var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
                return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
                    ? candidate : null;
            }

            if (path.StartsWith(@"\\") || Path.IsPathFullyQualified(path))
            {
                foreach (var root in new[] { Path.Combine(legacySystemRoot, "Upload"), sharedEvidenceRoot })
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return ExistingImage(root, path[prefix.Length..]) ?? placeholder;
                }
                return placeholder;
            }

            if (!path.StartsWith(@"\Upload\", StringComparison.OrdinalIgnoreCase)) return placeholder;
            var image = ExistingImage(legacySystemRoot, path[1..]);
            if (image is not null) return image;

            const string archivePrefix = @"\Upload\Evidence\";
            return sharedEvidenceRoot is not null && path.StartsWith(archivePrefix, StringComparison.OrdinalIgnoreCase)
                ? ExistingImage(sharedEvidenceRoot, path[archivePrefix.Length..]) ?? placeholder
                : placeholder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Form4/Form10 use File.Exists; malformed or missing records use noimage.
            return placeholder;
        }
    }
}
