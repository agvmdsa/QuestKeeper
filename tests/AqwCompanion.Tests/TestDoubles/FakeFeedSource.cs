using AqwCompanion.Core.Feed;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeFeedSource(FeedSnapshot? initial = null) : IFeedSource
{
    public FeedSnapshot Current { get; set; } = initial ?? FeedSnapshot.Empty;

    public event EventHandler? Changed;

    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
