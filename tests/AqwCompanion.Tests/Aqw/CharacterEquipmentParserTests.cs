using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Aqw;

public class CharacterEquipmentParserTests
{
    private readonly CharacterEquipmentParser _parser = new();

    private static PageSnapshot Page(string script)
    {
        var html = $$"""
            <html><body>
            <object><param name="FlashVars" value="strName=Test Hero&amp;intLevel=10&amp;strArmorName=Test Armor&amp;strWeaponName=Test Weapon" /></object>
            <script>{{script}}</script>
            </body></html>
            """;
        return PageSnapshot.Create("https://account.aq.com/CharPage?id=Test%20Hero", html, "1.0.0", DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Reads_the_character_id_from_the_inline_script()
    {
        var result = _parser.Parse(Page("var bLoadedBadges = false;\n        var ccid = 3;\n        $(document).ready(function () {});"));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal(3, result.Data!.CharacterId);
        Assert.Equal("Test Hero", result.Data.Name);
    }

    [Fact]
    public void The_character_id_is_null_when_the_script_has_none()
    {
        var result = _parser.Parse(Page("var other = 3;"));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Null(result.Data!.CharacterId);
    }

    [Theory]
    [InlineData("var ccid = abc;")]
    [InlineData("var ccid = 0;")]
    [InlineData("var ccid = 99999999999;")]
    public void An_unusable_character_id_is_ignored(string script)
    {
        var result = _parser.Parse(Page(script));

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Null(result.Data!.CharacterId);
    }
}
