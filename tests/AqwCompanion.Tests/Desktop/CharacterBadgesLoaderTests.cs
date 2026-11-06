using AqwCompanion.App.Badges;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Desktop;

public class CharacterBadgesLoaderTests
{
    private static PageFetchResult Page(string body) =>
        PageFetchResult.Success(PageSnapshot.Create(AqwUrls.Badges(3), body, "1.0.0", DateTimeOffset.UnixEpoch));

    private static (CharacterBadgesLoader Loader, FakePageFetcher Fetcher, InMemoryStore<CharacterBadges> Store) Build(
        Func<string, PageFetchResult> fetch)
    {
        var fetcher = new FakePageFetcher(fetch);
        var store = new InMemoryStore<CharacterBadges>();
        var ingestion = new IngestionService<CharacterBadges>(
            new BadgesParser(), store, new FailedSnapshotBuffer(), new ListLogger<IngestionService<CharacterBadges>>());
        return (new CharacterBadgesLoader(fetcher, ingestion), fetcher, store);
    }

    [Fact]
    public async Task Loads_the_badges_with_one_request_to_the_badges_url()
    {
        var (loader, fetcher, store) = Build(_ => Page("""[{"sTitle":"Test Badge","sDesc":"d","sCategory":"c"}]"""));

        var result = await loader.LoadAsync(3);

        Assert.Equal(BadgesLoadStatus.Loaded, result.Status);
        Assert.Equal(["Test Badge"], result.Badges!.Badges.Select(b => b.Title));
        Assert.Equal([AqwUrls.Badges(3)], fetcher.Requested);
        Assert.Single(store.Saved);
        Assert.True(result.Persisted);
    }

    [Fact]
    public async Task A_suspicious_answer_is_loaded_but_not_persisted()
    {
        var (loader, _, store) = Build(_ => Page("""[{"sDesc":"d","sCategory":"c"},{"sDesc":"e"}]"""));

        var result = await loader.LoadAsync(3);

        Assert.Equal(BadgesLoadStatus.Loaded, result.Status);
        Assert.False(result.Persisted);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task An_empty_list_is_loaded_with_zero_badges()
    {
        var (loader, _, _) = Build(_ => Page("[]"));

        var result = await loader.LoadAsync(3);

        Assert.Equal(BadgesLoadStatus.Loaded, result.Status);
        Assert.Equal(0, result.Badges!.Count);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("{\"not\":\"a list\"}")]
    [InlineData("[broken")]
    public async Task An_unreadable_answer_is_not_found(string body)
    {
        var (loader, _, store) = Build(_ => Page(body));

        var result = await loader.LoadAsync(3);

        Assert.Equal(BadgesLoadStatus.NotFound, result.Status);
        Assert.Null(result.Badges);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task A_failed_fetch_is_unavailable()
    {
        var (loader, _, _) = Build(_ => PageFetchResult.Failure("The request failed."));

        var result = await loader.LoadAsync(3);

        Assert.Equal(BadgesLoadStatus.Unavailable, result.Status);
        Assert.Null(result.Badges);
    }

    [Fact]
    public async Task A_throwing_fetcher_is_unavailable_instead_of_throwing()
    {
        var (loader, _, _) = Build(_ => throw new InvalidOperationException("boom"));

        var result = await loader.LoadAsync(3);

        Assert.Equal(BadgesLoadStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Cancellation_is_passed_on()
    {
        var (loader, _, _) = Build(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => loader.LoadAsync(3));
    }
}
