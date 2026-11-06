using GemBox.Spreadsheet;

namespace MachineKarte.Web.Services;

public static class LegacyReportExport
{
    private static readonly Dictionary<string, (DateTime Modified, long Length, ExcelFile Workbook)> Templates = new(StringComparer.OrdinalIgnoreCase);
    // ponytail: bounded process-local write locks; coordinate across processes if hosting multiple workers.
    private static readonly SemaphoreSlim[] OutputLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    static LegacyReportExport() => SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");

    public static string NormalizeProcess(string process)
    {
        var value = process?.Trim().ToUpperInvariant();
        return value is "DC" or "DB" or "WB" or "MP" or "PL" or "FT" or "FL" or "TP" or "SING" or "MEMS" or "EDS" or "ATC"
            ? value : throw new ArgumentException("A valid report process is required.");
    }

    public static string ReportPath(IConfiguration config, bool te, string process, string fileName)
    {
        process = NormalizeProcess(process);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 ||
            !(fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Invalid report file name.");

        var key = te ? "ReportOutput:TeTempRoot" : "ReportOutput:PmTempRoot";
        var root = config[key] ?? Path.Combine(LegacyEvidenceRoot(config, te), "TempData");
        if (!Path.IsPathFullyQualified(root)) throw new InvalidOperationException($"{key} must be an absolute path.");
        return Path.Combine(root, "Print Out", process, fileName);
    }

    public static async Task<string> SaveReportAsync(IConfiguration config, bool te, string process,
        string fileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        var path = ReportPath(config, te, process, fileName);
        var outputLock = OutputLocks[StringComparer.OrdinalIgnoreCase.GetHashCode(path) & (OutputLocks.Length - 1)];
        await outputLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            string VersionName() => $"{stem}__{Guid.NewGuid():N}{extension}";
            var version = VersionName();
            var temporary = Path.Combine(directory, version + ".tmp");
            try
            {
                // Retain pre-history output on first regeneration, including its timestamp.
                if (File.Exists(path) && !Directory.EnumerateFiles(directory, $"{stem}__*{extension}")
                    .Any(file => IsReportFile(Path.GetFileName(file), stem, extension.TrimStart('.'))))
                {
                    File.Copy(path, temporary);
                    File.Move(temporary, Path.Combine(directory, VersionName()));
                }
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                // Publish complete files only. Keep the legacy latest-file URL working as well.
                File.Move(temporary, Path.Combine(directory, version));
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return version;
        }
        finally { outputLock.Release(); }
    }

    public sealed record GeneratedFile(string FileName, DateTime GeneratedAtUtc, long SizeBytes);

    public static IReadOnlyList<GeneratedFile> ListReports(IConfiguration config, bool te, string process, string stem)
    {
        var directory = Path.GetDirectoryName(ReportPath(config, te, process, stem + ".xlsx"))!;
        var result = new List<GeneratedFile>();
        try
        {
            foreach (var format in new[] { "xlsx", "pdf" })
            {
                var versions = new DirectoryInfo(directory).EnumerateFiles($"{stem}__*.{format}")
                    .Where(file => Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name)[(stem.Length + 2)..], "N", out _))
                    .ToArray();
                if (versions.Length > 0)
                    result.AddRange(versions.Select(file => new GeneratedFile(file.Name, file.LastWriteTimeUtc, file.Length)));
                else
                {
                    var legacy = new FileInfo(Path.Combine(directory, $"{stem}.{format}"));
                    if (legacy.Exists) result.Add(new(legacy.Name, legacy.LastWriteTimeUtc, legacy.Length));
                }
            }
        }
        catch (DirectoryNotFoundException) { return Array.Empty<GeneratedFile>(); }
        return result.OrderByDescending(file => file.GeneratedAtUtc).ToArray();
    }

    public static bool IsReportFile(string fileName, string stem, string format)
    {
        if (fileName == $"{stem}.{format}") return true;
        var prefix = stem + "__";
        return fileName.Length == prefix.Length + 32 + format.Length + 1 &&
            fileName.StartsWith(prefix, StringComparison.Ordinal) && fileName.EndsWith("." + format, StringComparison.Ordinal) &&
            Guid.TryParseExact(fileName.Substring(prefix.Length, 32), "N", out _);
    }

    public static string NormalizeFormat(string format) => format?.ToLowerInvariant() switch
    {
        "xlsx" => "xlsx",
        "pdf" => "pdf",
        _ => throw new ArgumentException("Choose Excel or PDF output.")
    };

    public static string DownloadName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var separator = stem.LastIndexOf("__", StringComparison.Ordinal);
        return separator >= 0 && Guid.TryParseExact(stem[(separator + 2)..], "N", out _)
            ? stem[..separator] + Path.GetExtension(fileName) : fileName;
    }

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
