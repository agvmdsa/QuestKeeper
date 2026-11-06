using AqwCompanion.App;

namespace AqwCompanion.Tests.Shell;

public class LatestResultStoreTests
{
    [Fact]
    public async Task Starts_empty()
    {
        var store = new LatestResultStore<string>();

        Assert.Null(store.Latest);
        Assert.Null(await store.GetLastCountAsync());
    }

    [Fact]
    public async Task Keeps_only_the_last_value()
    {
        var store = new LatestResultStore<string>();

        await store.SaveAsync("first", 1);
        await store.SaveAsync("second", 2);

        Assert.Equal("second", store.Latest);
        Assert.Equal(2, await store.GetLastCountAsync());
    }

    [Fact]
    public async Task SeedCount_sets_the_baseline_only_when_there_is_none()
    {
        var store = new LatestResultStore<string>();

        store.SeedCount(300);
        Assert.Equal(300, await store.GetLastCountAsync());

        store.SeedCount(5);
        Assert.Equal(300, await store.GetLastCountAsync());

        var saved = new LatestResultStore<string>();
        await saved.SaveAsync("x", 2);
        saved.SeedCount(300);
        Assert.Equal(2, await saved.GetLastCountAsync());
    }

    [Fact]
    public async Task Clear_forgets_the_value_and_the_count()
    {
        var store = new LatestResultStore<string>();
        await store.SaveAsync("x", 2);

        store.Clear();

        Assert.Null(store.Latest);
        Assert.Null(await store.GetLastCountAsync());
    }
}
