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
        => EvidenceImageCandidates(storedPath, legacySystemRoot, te, sharedEvidenceRoot).FirstOrDefault(File.Exists)
            ?? Path.Combine(imageRoot, "noimage.png");

    // Validate paths without touching the network. Staging opens candidates directly,
    // avoiding a separate SMB existence request for every photograph.
    internal static IReadOnlyList<string> EvidenceImageCandidates(string storedPath, string legacySystemRoot,
        bool te = false, string? sharedEvidenceRoot = null)
    {
        if (string.IsNullOrEmpty(storedPath)) return [];

        var path = (te ? storedPath.Replace("~", "") : storedPath).Replace('/', '\\');
        try
        {
            string? Candidate(string root, string relative)
            {
                var parts = relative.Split('\\');
                if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains(':')))
                    return null;
                var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
                return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? candidate : null;
            }

            if (path.StartsWith(@"\\") || Path.IsPathFullyQualified(path))
            {
                foreach (var root in new[] { Path.Combine(legacySystemRoot, "Upload"), sharedEvidenceRoot })
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return Candidate(root, path[prefix.Length..]) is { } absolute ? [absolute] : [];
                }
                return [];
            }

            if (!path.StartsWith(@"\Upload\", StringComparison.OrdinalIgnoreCase)) return [];
            var candidates = new List<string>();
            if (Candidate(legacySystemRoot, path[1..]) is { } original) candidates.Add(original);

            const string archivePrefix = @"\Upload\Evidence\";
            if (sharedEvidenceRoot is not null && path.StartsWith(archivePrefix, StringComparison.OrdinalIgnoreCase)
                && Candidate(sharedEvidenceRoot, path[archivePrefix.Length..]) is { } archive)
                candidates.Add(archive);
            return candidates;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Form4/Form10 use File.Exists; malformed or missing records use noimage.
            return [];
        }
    }
}
