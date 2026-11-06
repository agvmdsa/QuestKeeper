using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.App;

/// <summary>In-memory store that keeps only the most recent value. Nothing is persisted.</summary>
public sealed class LatestResultStore<T> : IResultStore<T>
{
    private T? _latest;
    private int? _lastCount;

    public T? Latest => _latest;

    /// <summary>Sets the item count the drop guard compares against, but only when no count is known yet.</summary>
    public void SeedCount(int count) => _lastCount ??= count;

    public void Clear()
    {
        _latest = default;
        _lastCount = null;
    }

    public Task<int?> GetLastCountAsync(CancellationToken ct = default) => Task.FromResult(_lastCount);

    public Task SaveAsync(T data, int count, CancellationToken ct = default)
    {
        _latest = data;
        _lastCount = count;
        return Task.CompletedTask;
    }
}
