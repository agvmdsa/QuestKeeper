namespace AqwCompanion.Core.Ingestion;

public interface IResultStore<T>
{
    Task<int?> GetLastCountAsync(CancellationToken ct = default);
    Task SaveAsync(T data, int count, CancellationToken ct = default);
}
