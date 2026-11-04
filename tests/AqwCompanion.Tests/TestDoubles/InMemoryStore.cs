using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class InMemoryStore<T> : IResultStore<T>
{
    public List<(T Data, int Count)> Saved { get; } = [];
    public int? LastCount { get; set; }
    public Exception? ThrowOnGetLast { get; set; }
    public Exception? ThrowOnSave { get; set; }

    public Task<int?> GetLastCountAsync(CancellationToken ct = default) =>
        ThrowOnGetLast is null ? Task.FromResult(LastCount) : throw ThrowOnGetLast;

    public Task SaveAsync(T data, int count, CancellationToken ct = default)
    {
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        Saved.Add((data, count));
        LastCount = count;
        return Task.CompletedTask;
    }
}
