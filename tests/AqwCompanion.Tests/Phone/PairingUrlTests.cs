using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.Phone;

public class PairingUrlTests
{
    [Fact]
    public void Builds_the_exact_https_link_with_the_token_in_the_fragment()
    {
        Assert.Equal("https://my-pc.tail1234.ts.net/#t=abc_DEF-123", PairingUrl.Build("my-pc.tail1234.ts.net", "abc_DEF-123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("evil.com/path")]
    [InlineData("my pc.ts.net")]
    [InlineData("host:443")]
    [InlineData("localhost")]
    [InlineData("a.b@evil.com")]
    public void Refuses_a_name_that_fails_the_name_check(string name)
    {
        Assert.Throws<ArgumentException>(() => PairingUrl.Build(name, "token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a#b")]
    public void Refuses_an_empty_or_unsafe_token(string token)
    {
        Assert.Throws<ArgumentException>(() => PairingUrl.Build("my-pc.tail1234.ts.net", token));
    }
}
