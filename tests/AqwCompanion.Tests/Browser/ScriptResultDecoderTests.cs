using AqwCompanion.App.Browser;

namespace AqwCompanion.Tests.Browser;

public class ScriptResultDecoderTests
{
    [Fact]
    public void Json_encoded_string_is_unescaped()
    {
        var raw = "\"<html><body class=\\\"x\\\">Name With 'Quote' &amp; Spaces\\n\\u00e9</body></html>\"";

        Assert.Equal(
            "<html><body class=\"x\">Name With 'Quote' &amp; Spaces\né</body></html>",
            ScriptResultDecoder.DecodeString(raw));
    }

    [Fact]
    public void Plain_string_is_returned_unchanged()
    {
        Assert.Equal("<html><body>hi</body></html>", ScriptResultDecoder.DecodeString("<html><body>hi</body></html>"));
    }

    [Fact]
    public void Plain_string_that_merely_starts_with_a_quote_is_returned_unchanged()
    {
        Assert.Equal("\"not json", ScriptResultDecoder.DecodeString("\"not json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("undefined")]
    public void Missing_results_become_empty(string? raw)
    {
        Assert.Equal(string.Empty, ScriptResultDecoder.DecodeString(raw));
    }
}
