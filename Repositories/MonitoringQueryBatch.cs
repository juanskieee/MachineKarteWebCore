namespace MachineKarte.Web.Repositories;

internal static class MonitoringQueryBatch
{
    // Bound dashboard batches; interactive report generation does not use this queue.
    private const int BatchSize = 4;
    private static readonly SemaphoreSlim Slots = new(BatchSize);

    public static async IAsyncEnumerable<(T Item, TResult Result)> ReadAsync<T, TResult>(
        IEnumerable<T> items, Func<T, Task<TResult>> read,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Consume batches in input order to bound raw-row buffering.
        foreach (var batch in items.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await Task.WhenAll(batch.Select(item => RunAsync(() => read(item), cancellationToken)));
            for (var i = 0; i < batch.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (batch[i], results[i]);
            }
        }
    }

    public static async Task<TResult> RunAsync<TResult>(
        Func<Task<TResult>> read, CancellationToken cancellationToken = default)
    {
        await Slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await read();
        }
        finally { Slots.Release(); }
    }
}
