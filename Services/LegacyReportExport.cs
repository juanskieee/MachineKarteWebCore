using GemBox.Spreadsheet;
using System.Collections.Concurrent;

namespace MachineKarte.Web.Services;

public static class LegacyReportExport
{
    private sealed class Template
    {
        public DateTime Modified;
        public long Length;
        public ExcelFile? Workbook;
    }
    private static readonly ConcurrentDictionary<string, Template> Templates = new(StringComparer.OrdinalIgnoreCase);
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
        var template = Templates.GetOrAdd(file.FullName, _ => new Template());
        // Protect the shared source workbook without blocking unrelated templates.
        lock (template)
        {
            if (template.Workbook is null ||
                template.Modified != file.LastWriteTimeUtc || template.Length != file.Length)
            {
                template.Workbook = ExcelFile.Load(file.FullName);
                template.Modified = file.LastWriteTimeUtc;
                template.Length = file.Length;
            }
            return template.Workbook.Clone();
        }
    }

    public static byte[] Save(ExcelFile workbook, string format)
    {
        using var stream = new MemoryStream();
        // Evidence photos are already sized by the writer; avoid repeating image work at save time.
        workbook.Save(stream, NormalizeFormat(format) == "pdf"
            ? new PdfSaveOptions { SelectionType = SelectionType.ActiveSheet }
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

    // Per-export staging, not a cache between requests. Only share reads run in parallel;
    // the writer uses local copies and mutates its workbook sequentially.
    public sealed class StagedPhotos : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MachineKarte-Evidence-" + Guid.NewGuid().ToString("N"));
        public IReadOnlyDictionary<string, string> Paths { get; private set; } = new Dictionary<string, string>();

        private StagedPhotos() { }

        public static async Task<StagedPhotos> LoadAsync(IEnumerable<string> storedPaths,
            string legacySystemRoot, string imageRoot, bool te, string sharedEvidenceRoot,
            CancellationToken cancellationToken = default)
        {
            var photos = new StagedPhotos();
            try
            {
                Directory.CreateDirectory(photos._directory);
                var resolved = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
                var copies = new ConcurrentDictionary<string, Lazy<Task<string?>>>(StringComparer.OrdinalIgnoreCase);
                var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };
                var placeholder = Path.Combine(imageRoot, "noimage.png");
                await Parallel.ForEachAsync(storedPaths.Distinct(StringComparer.Ordinal), options, async (path, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var local = placeholder;
                    foreach (var candidate in EvidenceImageCandidates(path, legacySystemRoot, te, sharedEvidenceRoot))
                    {
                        var staged = await copies.GetOrAdd(candidate, source => new Lazy<Task<string?>>(
                            () => photos.CopyAsync(source, token))).Value;
                        if (staged is null) continue;
                        local = staged;
                        break;
                    }
                    resolved[path] = local;
                });
                photos.Paths = resolved;
                return photos;
            }
            catch
            {
                photos.Dispose();
                throw;
            }
        }

        private async Task<string?> CopyAsync(string path, CancellationToken cancellationToken)
        {
            FileStream source;
            try
            {
                source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Match File.Exists: unavailable candidates allow the next root or noimage.
                return null;
            }
            await using (source)
            {
                var target = Path.Combine(_directory, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, cancellationToken);
                return target;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
