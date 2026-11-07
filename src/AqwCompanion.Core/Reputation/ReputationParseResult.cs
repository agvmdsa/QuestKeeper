namespace AqwCompanion.Core.Reputation;

/// <summary>
/// The outcome of reading a reputation file: either a snapshot (with the number of entries that were skipped as invalid)
/// or an error message. Nothing from the file is ever in the error message.
/// </summary>
public sealed record ReputationParseResult
{
    public ReputationSnapshot? Snapshot { get; init; }

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool IsSuccess => Snapshot is not null;

    public static ReputationParseResult Success(ReputationSnapshot snapshot, int skipped) =>
        new() { Snapshot = snapshot, SkippedCount = skipped };

    public static ReputationParseResult Failure(string error) => new() { Error = error };
}
