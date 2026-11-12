using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaTextTests
{
    // Built at run time: a lone surrogate in a literal may be coerced by the compiler.
    private static readonly string High = ((char)0xD83D).ToString();
    private static readonly string Low = ((char)0xDE00).ToString();
    private static readonly string Smile = High + Low;

    [Fact]
    public void Lone_surrogates_are_removed_and_pairs_kept()
    {
        Assert.Equal("Hero", SkuaText.WithoutLoneSurrogates("He" + High + "ro"));
        Assert.Equal("Hero", SkuaText.WithoutLoneSurrogates("Hero" + High));
        Assert.Equal("Hero", SkuaText.WithoutLoneSurrogates(Low + "Hero"));
        Assert.Equal("a" + Smile + "b", SkuaText.WithoutLoneSurrogates("a" + Low + Smile + High + "b"));
        Assert.Equal("", SkuaText.WithoutLoneSurrogates(Low + High));
    }

    [Fact]
    public void Text_without_lone_surrogates_is_returned_as_is()
    {
        var text = "plain " + Smile;

        Assert.Same(text, SkuaText.WithoutLoneSurrogates(text));
    }

    [Theory]
    [InlineData(@"Could not find file 'C:\Users\Jane Doe\Documents\Skua\Scripts\Farm.cs'.", "Could not find file 'Farm.cs'.")]
    [InlineData("Could not find file \"C:/Users/jane/Farm.cs\".", "Could not find file \"Farm.cs\".")]
    [InlineData(@"Access to the path C:\Users\jane\AppData\Roaming\Skua\x.json is denied.", "Access to the path x.json is denied.")]
    [InlineData(@"Folder C:\Users\Jane Doe\Skua\Scripts\Farm.cs failed", "Folder Farm.cs failed")]
    [InlineData(@"Share \\pc\share\jane\Farm.cs failed", "Share Farm.cs failed")]
    [InlineData("Missing /home/jane/scripts/Farm.cs now", "Missing Farm.cs now")]
    [InlineData("Missing '/Users/jane/My Scripts/Farm.cs'", "Missing 'Farm.cs'")]
    [InlineData(@"In C:\Users\jane\Scripts\ there", "In Scripts there")]
    [InlineData("file:///C:/Users/jane/Farm.cs", "file:///Farm.cs")]
    public void Absolute_paths_are_reduced_to_their_file_name(string message, string expected)
    {
        Assert.Equal(expected, SkuaText.WithFileNamesOnly(message));
    }

    [Theory]
    [InlineData("Object reference not set to an instance of an object.")]
    [InlineData("Read/write failed at https://example.com/a/b")]
    [InlineData("Ratio 1/2 and a/b/c relative")]
    [InlineData("Value '/' is not valid")]
    [InlineData("")]
    public void Text_without_an_absolute_path_is_left_alone(string message)
    {
        Assert.Equal(message, SkuaText.WithFileNamesOnly(message));
    }
}
