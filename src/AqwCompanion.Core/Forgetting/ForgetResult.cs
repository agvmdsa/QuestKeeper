namespace AqwCompanion.Core.Forgetting;

/// <summary>The outcome of Forgetting: whether the stored data is gone and which features could not be forgotten.</summary>
public sealed record ForgetResult(bool StoredDataRemoved, IReadOnlyList<string> NotForgotten)
{
    /// <summary>True when the stored data was removed and every feature was forgotten.</summary>
    public bool IsComplete => StoredDataRemoved && NotForgotten.Count == 0;
}
