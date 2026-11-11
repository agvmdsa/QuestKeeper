using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaLogFormatterTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bought_names_the_item_and_adds_the_quantity_only_when_known()
    {
        Assert.Equal("Bought Dragon Blade x2", SkuaLogFormatter.Bought(T, "Dragon Blade", 7, 2).Text);
        Assert.Equal("Bought Dragon Blade", SkuaLogFormatter.Bought(T, "Dragon Blade", 7, null).Text);
        Assert.Equal(SkuaLogKind.Bought, SkuaLogFormatter.Bought(T, "Dragon Blade", 7, 2).Kind);
    }

    [Fact]
    public void An_unresolved_name_falls_back_to_the_id_and_never_fails()
    {
        Assert.Equal("Bought item #7", SkuaLogFormatter.Bought(T, null, 7, null).Text);
        Assert.Equal("Bought item #7", SkuaLogFormatter.Bought(T, "  ", 7, null).Text);
    }

    [Fact]
    public void Sold_shows_quantity_and_gold_with_thousands_separators_regardless_of_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            Assert.Equal("Sold Blood Orb x50 for 2,500 gold", SkuaLogFormatter.Sold(T, "Blood Orb", 3, 50, 2500, false).Text);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void Sold_for_adventure_coins_says_so()
    {
        Assert.Equal("Sold Cape x1 for 100 AC", SkuaLogFormatter.Sold(T, "Cape", 3, 1, 100, true).Text);
    }

    [Fact]
    public void Dropped_tells_a_drop_list_entry_from_a_direct_add()
    {
        Assert.Equal("Drop: Blood Orb", SkuaLogFormatter.Dropped(T, "Blood Orb", addedDirectly: false).Text);
        Assert.Equal("Received: Blood Orb", SkuaLogFormatter.Dropped(T, "Blood Orb", addedDirectly: true).Text);
        Assert.Equal("Drop: unknown item", SkuaLogFormatter.Dropped(T, null, addedDirectly: false).Text);
    }

    [Fact]
    public void The_simple_events_have_plain_text()
    {
        Assert.Equal("Map: battleon", SkuaLogFormatter.MapChanged(T, "battleon").Text);
        Assert.Equal("Died", SkuaLogFormatter.Died(T).Text);
        Assert.Equal("Logged out", SkuaLogFormatter.LoggedOut(T).Text);
        Assert.Equal("Logging in again", SkuaLogFormatter.Relogin(T, false).Text);
        Assert.Equal("Disconnected by the server, logging in again", SkuaLogFormatter.Relogin(T, true).Text);
        Assert.Equal("Script started: Farm.cs", SkuaLogFormatter.ScriptStarted(T, "Farm.cs").Text);
        Assert.Equal("Script stopped: Farm.cs", SkuaLogFormatter.ScriptStopped(T, "Farm.cs").Text);
    }

    [Fact]
    public void Quests_use_the_name_when_known_and_the_id_otherwise()
    {
        Assert.Equal("Quest accepted: Slay the Beast", SkuaLogFormatter.QuestAccepted(T, 5, "Slay the Beast").Text);
        Assert.Equal("Quest accepted: #5", SkuaLogFormatter.QuestAccepted(T, 5, null).Text);
        Assert.Equal("Quest turned in: #5", SkuaLogFormatter.QuestTurnedIn(T, 5, null).Text);
    }

    [Fact]
    public void An_error_text_loses_line_breaks_and_is_cut_to_the_cap()
    {
        var entry = SkuaLogFormatter.ScriptError(T, "line one\r\nline two " + new string('x', 400));

        Assert.DoesNotContain('\n', entry.Text);
        Assert.True(entry.Text.Length <= SkuaProtocol.MaxTextLength);
        Assert.StartsWith("Script stopped with an error: line one line two", entry.Text);
    }

    [Fact]
    public void Cutting_long_text_does_not_split_a_surrogate_pair()
    {
        var entry = SkuaLogFormatter.ScriptError(T, new string('a', 300 - "Script stopped with an error: ".Length - 1) + "\U0001F600");

        Assert.True(entry.Text.Length <= SkuaProtocol.MaxTextLength);
        Assert.False(char.IsHighSurrogate(entry.Text[^1]));
    }
}
