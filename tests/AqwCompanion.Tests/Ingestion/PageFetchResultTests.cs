using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Ingestion;

public class PageFetchResultTests
{
    [Fact]
    public void A_plain_failure_keeps_working_and_counts_as_a_network_failure_without_a_status()
    {
        var result = PageFetchResult.Failure("boom");

        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.Error);
        Assert.Equal(PageFailureKind.Network, result.Kind);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public void A_rich_failure_carries_its_kind_and_status()
    {
        var result = PageFetchResult.Failure("busy", PageFailureKind.Http, 503);

        Assert.Equal(PageFailureKind.Http, result.Kind);
        Assert.Equal(503, result.HttpStatus);
    }

    [Fact]
    public void A_success_has_no_failure_kind()
    {
        var result = PageFetchResult.Success(PageSnapshot.Create("https://x/", "p", "1", DateTimeOffset.UnixEpoch));

        Assert.True(result.IsSuccess);
        Assert.Equal(PageFailureKind.None, result.Kind);
        Assert.Null(result.HttpStatus);
    }
}
