using System.Text.RegularExpressions;
using AqwCompanion.App.Phone;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Web;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

// Needs web/dist: run "npm ci && npm run build" in web/ before building the App project.
public sealed class EmbeddedAssetSourceTests
{
    private const string Hint = "run npm run build in web/";

    private static EmbeddedAssetSource Load() => new(typeof(EmbeddedAssetSource).Assembly);

    private static readonly Regex ETagShape = new("^\"[0-9A-F]{16}\"$");

    [Theory]
    [InlineData("/index.html", "text/html; charset=utf-8")]
    [InlineData("/sw.js", "text/javascript; charset=utf-8")]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    [InlineData("/icons/icon-192.png", "image/png")]
    public void The_page_files_are_embedded_with_their_type_and_an_etag(string path, string type)
    {
        var asset = Load().Find(path);

        Assert.True(asset is not null, $"{path} is not embedded: {Hint}");
        Assert.Equal(type, asset!.ContentType);
        Assert.Matches(ETagShape, asset.Etag);
        Assert.NotEmpty(asset.Bytes);
    }

    [Fact]
    public void The_hashed_script_and_style_are_embedded()
    {
        var source = Load();
        var script = source.Paths.FirstOrDefault(p => p.StartsWith("/assets/", StringComparison.Ordinal) && p.EndsWith(".js", StringComparison.Ordinal));
        var style = source.Paths.FirstOrDefault(p => p.StartsWith("/assets/", StringComparison.Ordinal) && p.EndsWith(".css", StringComparison.Ordinal));

        Assert.True(script is not null, $"no /assets/*.js is embedded: {Hint}");
        Assert.True(style is not null, $"no /assets/*.css is embedded: {Hint}");
        Assert.Equal("text/javascript; charset=utf-8", source.Find(script!)!.ContentType);
        Assert.Equal("text/css; charset=utf-8", source.Find(style!)!.ContentType);
    }

    [Theory]
    [InlineData("/package.json")]
    [InlineData("/src/main.ts")]
    [InlineData("/dev/mock-api.ts")]
    [InlineData("/test/state.test.ts")]
    public void Sources_and_tooling_are_not_embedded(string path) =>
        Assert.Null(Load().Find(path));

    [Fact]
    public void Two_loads_give_the_same_etag()
    {
        var first = Load().Find("/index.html");
        Assert.True(first is not null, $"/index.html is not embedded: {Hint}");
        Assert.Equal(first!.Etag, Load().Find("/index.html")!.Etag);
    }

    [Fact]
    public void The_built_page_is_served_by_the_real_handler()
    {
        var source = Load();
        var script = source.Paths.FirstOrDefault(p => p.StartsWith("/assets/", StringComparison.Ordinal) && p.EndsWith(".js", StringComparison.Ordinal));
        Assert.True(script is not null, $"no /assets/*.js is embedded: {Hint}");

        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), () => DateTimeOffset.UtcNow);
        using var hub = new PhoneHub(new FakeFeedSource(), new NoticeEngine(), new NoticeLog(50), () => DateTimeOffset.UtcNow);
        var handler = new PhoneHandler(registry, hub, source, h => h == "127.0.0.1:47822", "9.9.9");
        var headers = new Dictionary<string, string> { ["Host"] = "127.0.0.1:47822" };

        var page = handler.Handle(new PhoneRequest("GET", "/", headers, ""));
        var asset = handler.Handle(new PhoneRequest("GET", script!, headers, ""));

        Assert.Equal(200, page.Status);
        Assert.Contains(page.Headers, h => h.Key == "Content-Security-Policy");
        Assert.Equal(200, asset.Status);
        Assert.StartsWith("text/javascript", asset.ContentType);
    }
}
