using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class InMemoryStore<T> : IResultStore<T>
{
    public List<(T Data, int Count)> Saved { get; } = [];
    public int? LastCount { get; set; }

    public Task<int?> GetLastCountAsync(CancellationToken ct = default) => Task.FromResult(LastCount);

    public Task SaveAsync(T data, int count, CancellationToken ct = default)
    {
        Saved.Add((data, count));
        LastCount = count;
        return Task.CompletedTask;
    }
}
