using AqwCompanion.App;
using AqwCompanion.App.Feed;

namespace AqwCompanion.Tests.Feed;

public sealed class FeedSecretStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    private FeedSecretStore Store() => new(_paths.FeedTokenPath);

    [Fact]
    public void The_secret_is_made_once_as_url_safe_text_of_32_random_bytes()
    {
        var token = Store().GetOrCreate();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.Equal(token, File.ReadAllText(_paths.FeedTokenPath));
    }

    [Fact]
    public void A_new_store_reads_the_same_secret_from_the_file()
    {
        var first = Store().GetOrCreate();

        Assert.Equal(first, Store().GetOrCreate());
    }

    [Fact]
    public void The_same_store_keeps_returning_the_same_secret()
    {
        var store = Store();

        Assert.Equal(store.GetOrCreate(), store.GetOrCreate());
    }

    [Fact]
    public void Regenerating_changes_the_secret_and_the_file()
    {
        var store = Store();
        var old = store.GetOrCreate();

        var fresh = store.Regenerate();

        Assert.NotEqual(old, fresh);
        Assert.Equal(fresh, File.ReadAllText(_paths.FeedTokenPath));
        Assert.Equal(fresh, store.GetOrCreate());
        Assert.Equal(fresh, Store().GetOrCreate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("this contains spaces and is not a valid secret at all")]
    public void A_damaged_file_is_replaced_by_a_new_secret(string content)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.FeedTokenPath, content);

        var token = Store().GetOrCreate();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.Equal(token, File.ReadAllText(_paths.FeedTokenPath));
    }

    [Fact]
    public void The_bytes_compared_by_the_feed_are_the_utf8_of_the_text()
    {
        var store = Store();

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(store.GetOrCreate()), store.GetOrCreateBytes());
    }

    [Fact]
    public void Two_stores_on_different_files_have_different_secrets()
    {
        var other = new FeedSecretStore(Path.Combine(_paths.Root, "other.token"));

        Assert.NotEqual(Store().GetOrCreate(), other.GetOrCreate());
    }

    [Fact]
    public void No_temp_file_is_left_behind()
    {
        Store().Regenerate();

        Assert.Equal(["feed.token"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }
}
