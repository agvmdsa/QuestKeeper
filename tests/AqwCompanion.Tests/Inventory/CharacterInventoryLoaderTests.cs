using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Site;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Inventory;

public class CharacterInventoryLoaderTests
{
    private static PageFetchResult Page(string body) =>
        PageFetchResult.Success(PageSnapshot.Create(AqwUrls.Inventory(3), body, "1.0.0", DateTimeOffset.UnixEpoch));

    private static (CharacterInventoryLoader Loader, FakePageFetcher Fetcher, InMemoryStore<CharacterInventory> Store) Build(
        Func<string, PageFetchResult> fetch)
    {
        var fetcher = new FakePageFetcher(fetch);
        var store = new InMemoryStore<CharacterInventory>();
        var ingestion = new IngestionService<CharacterInventory>(
            new InventoryParser(), store, new FailedSnapshotBuffer(), new ListLogger<IngestionService<CharacterInventory>>());
        return (new CharacterInventoryLoader(fetcher, ingestion), fetcher, store);
    }

    [Fact]
    public async Task Loads_the_inventory_with_one_request_to_the_inventory_url()
    {
        var (loader, fetcher, store) = Build(_ => Page("""[{"strName":"Test Item","strType":"Item","intCount":4}]"""));

        var result = await loader.LoadAsync(3);

        Assert.Equal(InventoryLoadStatus.Loaded, result.Status);
        Assert.Equal(["Test Item"], result.Inventory!.Items.Select(i => i.Name));
        Assert.Equal([AqwUrls.Inventory(3)], fetcher.Requested);
        Assert.Single(store.Saved);
        Assert.True(result.Persisted);
    }

    [Fact]
    public async Task A_suspicious_answer_is_loaded_but_not_persisted()
    {
        var (loader, _, store) = Build(_ => Page("""[{"strType":"Armor"},{"intCount":2}]"""));

        var result = await loader.LoadAsync(3);

        Assert.Equal(InventoryLoadStatus.Loaded, result.Status);
        Assert.False(result.Persisted);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task An_empty_list_is_loaded_and_persisted_with_zero_items()
    {
        var (loader, _, _) = Build(_ => Page("[]"));

        var result = await loader.LoadAsync(3);

        Assert.Equal(InventoryLoadStatus.Loaded, result.Status);
        Assert.Equal(0, result.Inventory!.Count);
        Assert.True(result.Persisted);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("{\"not\":\"a list\"}")]
    [InlineData("[broken")]
    public async Task An_unreadable_answer_is_not_found(string body)
    {
        var (loader, _, store) = Build(_ => Page(body));

        var result = await loader.LoadAsync(3);

        Assert.Equal(InventoryLoadStatus.NotFound, result.Status);
        Assert.Null(result.Inventory);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task A_failed_fetch_is_unavailable()
    {
        var (loader, _, _) = Build(_ => PageFetchResult.Failure("The request failed."));

        var result = await loader.LoadAsync(3);

        Assert.Equal(InventoryLoadStatus.Unavailable, result.Status);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public async Task A_throwing_fetcher_is_unavailable_instead_of_throwing()
    {
        var (loader, _, _) = Build(_ => throw new InvalidOperationException("boom"));

        Assert.Equal(InventoryLoadStatus.Unavailable, (await loader.LoadAsync(3)).Status);
    }

    [Fact]
    public async Task Cancellation_is_passed_on()
    {
        var (loader, _, _) = Build(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => loader.LoadAsync(3));
    }
}
