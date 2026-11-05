using AqwCompanion.App;

namespace AqwCompanion.Tests.Desktop;

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
}
