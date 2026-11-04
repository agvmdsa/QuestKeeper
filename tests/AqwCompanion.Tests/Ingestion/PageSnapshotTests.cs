using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Ingestion;

public class PageSnapshotTests
{
    [Fact]
    public void Create_strips_query_and_fragment_from_url()
    {
        var snapshot = PageSnapshot.Create(
            "https://account.aq.com/AQW/Inventory?id=123&token=abc#top",
            "<html></html>",
            "1.2.3",
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("https://account.aq.com/AQW/Inventory", snapshot.Url);
        Assert.Equal("<html></html>", snapshot.Payload);
        Assert.Equal("1.2.3", snapshot.AppVersion);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), snapshot.CapturedAt);
    }

    [Fact]
    public void Create_rejects_relative_urls()
    {
        Assert.Throws<UriFormatException>(() =>
            PageSnapshot.Create("/AQW/Inventory", "", "1.0.0", DateTimeOffset.UtcNow));
    }
}
