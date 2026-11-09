namespace AqwCompanion.Core.Feed;

/// <summary>Where the feed gets its snapshot from. <see cref="Changed"/> may be raised from any thread.</summary>
public interface IFeedSource
{
    FeedSnapshot Current { get; }

    event EventHandler? Changed;
}
