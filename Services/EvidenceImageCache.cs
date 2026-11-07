using System.Collections.Concurrent;

namespace MachineKarte.Web.Services;

// Each export gets a short-lived local copy of its photos. Workbook mapping stays sequential;
// only independent network reads run concurrently, without sharing a mutable workbook.
public sealed class EvidenceImageCache : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MachineKarte-Evidence-" + Guid.NewGuid().ToString("N"));
    public IReadOnlyDictionary<string, string> Paths { get; private set; } = new Dictionary<string, string>();

    public static async Task<EvidenceImageCache> LoadAsync(IEnumerable<string> storedPaths,
        string legacySystemRoot, string imageRoot, bool te, string sharedEvidenceRoot,
        CancellationToken cancellationToken = default)
    {
        var cache = new EvidenceImageCache();
        try
        {
            Directory.CreateDirectory(cache._directory);
            var resolved = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            var copies = new ConcurrentDictionary<string, Lazy<Task<string?>>>(StringComparer.OrdinalIgnoreCase);
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };
            var placeholder = Path.Combine(imageRoot, "noimage.png");
            await Parallel.ForEachAsync(storedPaths.Distinct(StringComparer.Ordinal), options, async (path, token) =>
            {
                token.ThrowIfCancellationRequested();
                var local = placeholder;
                foreach (var candidate in LegacyReportExport.EvidenceImageCandidates(path, legacySystemRoot, te, sharedEvidenceRoot))
                {
                    var staged = await copies.GetOrAdd(candidate, source => new Lazy<Task<string?>>(
                        () => cache.CopyAsync(source, token))).Value;
                    if (staged is null) continue;
                    local = staged;
                    break;
                }
                resolved[path] = local;
            });
            cache.Paths = resolved;
            return cache;
        }
        catch
        {
            cache.Dispose();
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
