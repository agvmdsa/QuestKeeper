using System.Text.Json;
using AqwCompanion.Core.Common;

namespace AqwCompanion.Tests.Common;

public class JsonElementReaderTests
{
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void ReadCleanText_gives_the_cleaned_string()
    {
        Assert.Equal("Sword", Parse("""{"n":"  Sw\u0000ord "}""").ReadCleanText("n", 50));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"n":5}""")]
    [InlineData("""{"n":null}""")]
    [InlineData("""{"n":"   "}""")]
    [InlineData("""{"n":"\u0001"}""")]
    public void ReadCleanText_is_null_when_missing_not_a_string_or_blank(string json)
    {
        Assert.Null(Parse(json).ReadCleanText("n", 50));
    }

    [Fact]
    public void ReadCleanText_caps_without_splitting_a_surrogate_pair()
    {
        var element = Parse("""{"n":"ab😀cd"}""");

        Assert.Equal("ab", element.ReadCleanText("n", 3));
    }

    [Theory]
    [InlineData("""{"c":0}""", 0)]
    [InlineData("""{"c":7}""", 7)]
    [InlineData("""{"c":-1}""", null)]
    [InlineData("""{"c":1.5}""", null)]
    [InlineData("""{"c":3000000000}""", null)]
    [InlineData("""{"c":"7"}""", null)]
    [InlineData("""{}""", null)]
    public void ReadNonNegativeInt32_reads_only_whole_non_negative_numbers(string json, int? expected)
    {
        Assert.Equal(expected, Parse(json).ReadNonNegativeInt32("c"));
    }
}
