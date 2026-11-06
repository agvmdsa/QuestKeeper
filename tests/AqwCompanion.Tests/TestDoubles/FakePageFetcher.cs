using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakePageFetcher(Func<string, PageFetchResult> fetch) : IPageFetcher
{
    public List<string> Requested { get; } = [];

    public Task<PageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        Requested.Add(url);
        return Task.FromResult(fetch(url));
    }
}
