using System.Reflection;
using AqwCompanion.Core.Site;

namespace AqwCompanion.Tests.Site;

public class AqwUrlsTests
{
    public static TheoryData<string> AllPageUrls()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(AqwUrls).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.Name != nameof(AqwUrls.AccountHost))
            {
                data.Add((string)field.GetValue(null)!);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPageUrls))]
    public void Every_page_constant_is_an_absolute_https_url_on_the_account_host(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);

        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal(AqwUrls.AccountHost, uri.Host);
    }

    [Fact]
    public void Known_pages_have_the_expected_addresses()
    {
        Assert.Equal("account.aq.com", AqwUrls.AccountHost);
        Assert.Equal("https://account.aq.com/", AqwUrls.Root);
        Assert.Equal("https://account.aq.com/Home", AqwUrls.Home);
        Assert.Equal("https://account.aq.com/AQW/BuySeasonal", AqwUrls.BuySeasonal);
        Assert.Equal("https://account.aq.com/AQW/InventoryAwesome", AqwUrls.InventoryAwesome);
        Assert.Equal("https://account.aq.com/AQW/BuyBack", AqwUrls.BuyBack);
        Assert.Equal("https://account.aq.com/AQW/Progress", AqwUrls.Progress);
        Assert.Equal("https://account.aq.com/AQW/ProgressPlaytime", AqwUrls.ProgressPlaytime);
        Assert.Equal("https://account.aq.com/AQW/ProgressSagas", AqwUrls.ProgressSagas);
        Assert.Equal("https://account.aq.com/AQW/ProgressMembership", AqwUrls.ProgressMembership);
        Assert.Equal("https://account.aq.com/AQW/ProgressCoins", AqwUrls.ProgressCoins);
        Assert.Equal("https://account.aq.com/CharPage", AqwUrls.CharPageBase);
    }

    [Theory]
    [InlineData("Test Hero", "https://account.aq.com/CharPage?id=Test%20Hero")]
    [InlineData("O'Brien", "https://account.aq.com/CharPage?id=O%27Brien")]
    [InlineData("A & B", "https://account.aq.com/CharPage?id=A%20%26%20B")]
    [InlineData("Héroï", "https://account.aq.com/CharPage?id=H%C3%A9ro%C3%AF")]
    public void Char_page_escapes_the_character_name(string name, string expected)
    {
        Assert.Equal(expected, AqwUrls.CharPage(name));
    }

    [Theory]
    [InlineData("https://account.aq.com/Home", true)]
    [InlineData("https://account.aq.com/home", true)]
    [InlineData("https://ACCOUNT.AQ.COM/Home/", true)]
    [InlineData("https://account.aq.com/Home?x=1#top", true)]
    [InlineData("https://account.aq.com/Home/Settings", false)]
    [InlineData("https://account.aq.com/Login", false)]
    [InlineData("https://account.aq.com/", false)]
    [InlineData("https://evil.example.com/Home", false)]
    [InlineData("http://account.aq.com.evil.example/Home", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void Is_account_home_matches_only_the_home_page_on_the_account_host(string url, bool expected)
    {
        Assert.Equal(expected, AqwUrls.IsAccountHome(url));
    }

    [Fact]
    public void Badges_addresses_the_badges_list_by_character_id()
    {
        Assert.Equal("https://account.aq.com/CharPage/Badges?ccid=3", AqwUrls.Badges(3));
        Assert.True(AqwUrls.IsBadges(AqwUrls.Badges(123456)));
    }

    [Theory]
    [InlineData("https://account.aq.com/CharPage/Badges", true)]
    [InlineData("https://ACCOUNT.AQ.COM/charpage/badges/?ccid=3", true)]
    [InlineData("https://account.aq.com/CharPage", false)]
    [InlineData("https://account.aq.com/CharPage/Inventory?ccid=3", false)]
    [InlineData("https://evil.example.com/CharPage/Badges", false)]
    [InlineData("http://account.aq.com/CharPage/Badges", false)]
    [InlineData("not a url", false)]
    public void Is_badges_matches_only_the_badges_endpoint_on_the_account_host(string url, bool expected)
    {
        Assert.Equal(expected, AqwUrls.IsBadges(url));
    }
}
