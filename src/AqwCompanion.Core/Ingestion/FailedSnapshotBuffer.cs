namespace AqwCompanion.Core.Ingestion;

/// <summary>Holds, in memory only, the most recent snapshot whose ingestion was not Ok. Never written to disk automatically.</summary>
public sealed class FailedSnapshotBuffer
{
    private PageSnapshot? _latest;

    public PageSnapshot? Latest => Volatile.Read(ref _latest);

    public void Record(PageSnapshot snapshot) => Volatile.Write(ref _latest, snapshot);

    public void Clear() => Volatile.Write(ref _latest, null);
}
