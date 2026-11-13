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
    public void Dropped_carries_the_item_and_a_count_of_one_when_the_name_is_known()
    {
        Assert.Equal(new SkuaLogEntry(T, SkuaLogKind.Dropped, "Drop: Blood Orb", "Blood Orb", 1), SkuaLogFormatter.Dropped(T, "Blood Orb", false));
        Assert.Equal(new SkuaLogEntry(T, SkuaLogKind.Dropped, "Received: Blood Orb", "Blood Orb", 1), SkuaLogFormatter.Dropped(T, "Blood Orb", true));
        Assert.Null(SkuaLogFormatter.Dropped(T, null, false).Item);
        Assert.Null(SkuaLogFormatter.Dropped(T, "  ", false).Item);
        Assert.Equal(1, SkuaLogFormatter.Dropped(T, null, false).Count);
    }

    [Fact]
    public void The_dropped_item_is_one_line_and_cut_to_the_text_cap()
    {
        var entry = SkuaLogFormatter.Dropped(T, "Blood\r\nOrb" + new string('o', 400), false);

        Assert.StartsWith("Blood Orb", entry.Item);
        Assert.Equal(SkuaProtocol.MaxTextLength, entry.Item!.Length);
        Assert.True(entry.Text.Length <= SkuaProtocol.MaxTextLength);
    }

    [Fact]
    public void WithCount_writes_the_count_moves_the_time_and_keeps_kind_and_item()
    {
        var drop = SkuaLogFormatter.Dropped(T, "Blood Orb", false);
        var received = SkuaLogFormatter.Dropped(T, "Blood Orb", true);
        var later = T.AddSeconds(30);

        Assert.Equal(new SkuaLogEntry(later, SkuaLogKind.Dropped, "Drop: Blood Orb x13", "Blood Orb", 13), SkuaLogFormatter.WithCount(drop, 13, later));
        Assert.Equal(new SkuaLogEntry(later, SkuaLogKind.Dropped, "Received: Blood Orb x2", "Blood Orb", 2), SkuaLogFormatter.WithCount(received, 2, later));
        Assert.Equal(new SkuaLogEntry(later, SkuaLogKind.Dropped, "Drop: Blood Orb", "Blood Orb", 1), SkuaLogFormatter.WithCount(drop, 1, later));
    }

    [Fact]
    public void WithCount_keeps_the_count_within_1_to_99999_and_the_text_within_the_cap()
    {
        var drop = SkuaLogFormatter.Dropped(T, new string('o', 400), false);

        Assert.Equal(SkuaProtocol.MaxCount, SkuaLogFormatter.WithCount(drop, 250000, T).Count);
        Assert.Equal(1, SkuaLogFormatter.WithCount(drop, 0, T).Count);
        Assert.True(SkuaLogFormatter.WithCount(drop, 99999, T).Text.Length <= SkuaProtocol.MaxTextLength);
    }

    [Fact]
    public void DropKind_tells_a_drop_from_a_received_item_and_anything_else()
    {
        Assert.Equal("Drop", SkuaLogFormatter.DropKind(SkuaLogFormatter.Dropped(T, "X", false)));
        Assert.Equal("Received", SkuaLogFormatter.DropKind(SkuaLogFormatter.Dropped(T, "X", true)));
        Assert.Null(SkuaLogFormatter.DropKind(SkuaLogFormatter.Died(T)));
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
