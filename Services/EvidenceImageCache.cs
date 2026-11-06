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
            var resolved = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(storedPaths.Distinct(StringComparer.Ordinal), options, (path, token) =>
            {
                token.ThrowIfCancellationRequested();
                resolved[path] = LegacyReportExport.ResolveEvidenceImage(path, legacySystemRoot, imageRoot, te, sharedEvidenceRoot);
                return ValueTask.CompletedTask;
            });
            Directory.CreateDirectory(cache._directory);
            var local = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var placeholder = Path.Combine(imageRoot, "noimage.png");
            await Parallel.ForEachAsync(resolved.Values.Distinct(StringComparer.OrdinalIgnoreCase), options, async (path, token) =>
            {
                if (path.Equals(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    local[path] = path;
                    return;
                }
                var target = Path.Combine(cache._directory, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, token);
                local[path] = target;
            });
            cache.Paths = resolved.ToDictionary(pair => pair.Key, pair => local[pair.Value], StringComparer.Ordinal);
            return cache;
        }
        catch
        {
            cache.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
