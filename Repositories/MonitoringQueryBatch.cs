namespace MachineKarte.Web.Repositories;

internal static class MonitoringQueryBatch
{
    // Bound SQL concurrency across all three summaries, including concurrent users.
    private const int BatchSize = 4;
    private static readonly SemaphoreSlim Slots = new(BatchSize);

    public static async IAsyncEnumerable<(T Item, TResult Result)> ReadAsync<T, TResult>(
        IEnumerable<T> items, Func<T, Task<TResult>> read)
    {
        // Consume batches in input order to bound raw-row buffering.
        foreach (var batch in items.Chunk(BatchSize))
        {
            var results = await Task.WhenAll(batch.Select(item => ReadOneAsync(item, read)));
            for (var i = 0; i < batch.Length; i++)
                yield return (batch[i], results[i]);
        }
    }

    private static async Task<TResult> ReadOneAsync<T, TResult>(T item, Func<T, Task<TResult>> read)
    {
        await Slots.WaitAsync();
        try { return await read(item); }
        finally { Slots.Release(); }
    }
}
