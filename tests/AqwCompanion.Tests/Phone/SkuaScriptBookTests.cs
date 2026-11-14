using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class SkuaScriptBookTests
{
    private const string A = "0123456789abcdef";
    private const string B = "fedcba9876543210";
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(T0);
    private readonly MemorySkuaScriptFile _file = new();

    private SkuaScriptPrefs Prefs() => new(_file, _clock.GetUtcNow);

    private SkuaOptionCache Cache() => new(_file, _clock.GetUtcNow);

    private static readonly SkuaOptionDef Follow = new("Options", "Follow", "Player to follow", "Who", "string", "", null);
    private static readonly SkuaOptionDef Mode = new("Options", "Mode", "Mode", "", "enum", "Fast Farm", ["Fast Farm", "Slow"]);

    [Fact]
    public void A_script_asks_for_options_by_default_and_nothing_is_written()
    {
        var prefs = Prefs();

        Assert.True(prefs.AskOptions(A));
        Assert.Equal(new Dictionary<string, bool> { [A] = true, [B] = true }, prefs.AskOptions([A, B, A]));
        Assert.Equal(0, _file.Writes);
    }

    [Fact]
    public void A_choice_is_saved_and_read_back_by_a_new_instance()
    {
        Prefs().Set(A, false);

        Assert.Equal($"{{\"version\":1,\"scripts\":{{\"{A}\":{{\"value\":false,\"lastUsed\":\"2026-11-13T12:00:00+00:00\"}}}}}}", _file.Text);
        var again = Prefs();
        Assert.False(again.AskOptions(A));
        Assert.True(again.AskOptions(B));
    }

    [Fact]
    public void Asking_again_removes_the_entry()
    {
        var prefs = Prefs();
        prefs.Set(A, false);

        prefs.Set(A, true);

        Assert.True(Prefs().AskOptions(A));
        Assert.DoesNotContain(A, _file.Text);
    }

    [Fact]
    public void Ids_that_are_not_script_ids_are_never_kept()
    {
        var prefs = Prefs();

        prefs.Set("../../evil", false);
        prefs.Set("0123456789ABCDEF", false);

        Assert.True(prefs.AskOptions("../../evil"));
        Assert.Null(_file.Text);
    }

    [Fact]
    public void An_id_with_a_trailing_newline_is_not_a_script_id_nor_kept_nor_loaded()
    {
        Assert.False(SkuaScriptPrefs.IsScriptId(A + "\n"));
        Assert.True(SkuaScriptPrefs.IsScriptId(A));

        Prefs().Set(A + "\n", false);
        Assert.Null(_file.Text);

        _file.Text = "{\"version\":1,\"scripts\":{\"" + A + "\\n\":{\"value\":false,\"lastUsed\":\"2026-11-13T12:00:00+00:00\"}}}";
        var prefs = Prefs();
        Assert.True(prefs.AskOptions(A + "\n"));
        Assert.True(prefs.AskOptions(A));

        _file.Text = "{\"version\":1,\"scripts\":{\"" + A + "\\n\":{\"value\":[],\"lastUsed\":\"2026-11-13T12:00:00+00:00\"}}}";
        Assert.Null(Cache().Get(A + "\n"));
    }

    [Fact]
    public void An_entry_unused_for_90_days_is_dropped_and_a_used_one_kept()
    {
        var prefs = Prefs();
        prefs.Set(A, false);
        prefs.Set(B, false);

        _clock.Advance(TimeSpan.FromDays(60));
        Assert.False(prefs.AskOptions(B));   // a use: B's 90 days start again
        _clock.Advance(TimeSpan.FromDays(31));

        var later = Prefs();
        Assert.True(later.AskOptions(A));
        Assert.False(later.AskOptions(B));
    }

    [Fact]
    public void Expired_entries_leave_the_file_on_the_next_save()
    {
        var prefs = Prefs();
        prefs.Set(A, false);
        _clock.Advance(TimeSpan.FromDays(91));

        prefs.Set(B, false);

        Assert.DoesNotContain(A, _file.Text);
        Assert.Contains(B, _file.Text);
    }

    [Fact]
    public void Listing_touches_entries_at_most_once_an_hour()
    {
        var prefs = Prefs();
        prefs.Set(A, false);
        var writes = _file.Writes;

        prefs.AskOptions([A, B]);
        _clock.Advance(TimeSpan.FromMinutes(30));
        prefs.AskOptions([A]);
        Assert.Equal(writes, _file.Writes);

        _clock.Advance(TimeSpan.FromMinutes(31));
        prefs.AskOptions([A]);
        Assert.Equal(writes + 1, _file.Writes);
    }

    [Fact]
    public void Forget_clears_the_choices_and_deletes_the_file()
    {
        var prefs = Prefs();
        prefs.Set(A, false);

        prefs.Quiesce();
        prefs.Forget();

        Assert.True(prefs.AskOptions(A));
        Assert.Null(_file.Text);
        Assert.Equal(1, _file.Deletes);
        prefs.Set(B, false);   // forgetting ends the quiesce
        Assert.Contains(B, _file.Text);
    }

    [Fact]
    public void While_quiesced_nothing_is_written_and_resume_writes_again()
    {
        var prefs = Prefs();
        prefs.Quiesce();
        prefs.Set(A, false);
        Assert.Null(_file.Text);

        prefs.Resume();
        prefs.Set(B, false);
        Assert.Contains(B, _file.Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":1,\"scripts\":[]}")]
    [InlineData("{\"version\":1,\"scripts\":{\"0123456789abcdef\":{\"value\":\"no\",\"lastUsed\":\"2026-11-13T12:00:00+00:00\"}}}")]
    public void A_damaged_file_is_no_choices(string text)
    {
        _file.Text = text;

        Assert.True(Prefs().AskOptions(A));
    }

    [Fact]
    public void Entries_with_hostile_ids_in_the_file_are_dropped()
    {
        _file.Text = "{\"version\":1,\"scripts\":{\"../x\":{\"value\":false,\"lastUsed\":\"2026-11-13T12:00:00+00:00\"},"
            + $"\"{A}\":{{\"value\":false,\"lastUsed\":\"2026-11-13T12:00:00+00:00\"}}}}}}";

        var prefs = Prefs();

        Assert.False(prefs.AskOptions(A));
        Assert.True(prefs.AskOptions("../x"));
    }

    [Fact]
    public void The_option_cache_keeps_definitions_per_script_and_reads_them_back()
    {
        Cache().Set(A, [Follow, Mode]);

        var again = Cache();
        Assert.Equal([Follow, Mode], again.Get(A));
        Assert.Null(again.Get(B));
        Assert.Contains("\"choices\":[\"Fast Farm\",\"Slow\"]", _file.Text);
    }

    [Fact]
    public void An_empty_definition_list_is_kept_as_a_script_without_options()
    {
        Cache().Set(A, []);

        Assert.Empty(Cache().Get(A)!);
    }

    [Fact]
    public void The_option_cache_expires_and_forgets_the_same_way()
    {
        var cache = Cache();
        cache.Set(A, [Follow]);
        cache.Set(B, [Mode]);
        _clock.Advance(TimeSpan.FromDays(80));
        cache.Touch([B]);
        _clock.Advance(TimeSpan.FromDays(11));

        Assert.Null(Cache().Get(A));
        Assert.Equal([Mode], Cache().Get(B));

        cache.Forget();
        Assert.Null(cache.Get(B));
        Assert.Null(_file.Text);
    }

    [Fact]
    public void Cached_definitions_from_the_file_are_cleaned()
    {
        _file.Text = $"{{\"version\":1,\"scripts\":{{\"{A}\":{{\"value\":["
            + "{\"category\":\"Options\",\"name\":\"Follow\",\"displayName\":\"F\",\"description\":\"\",\"type\":\"string\",\"default\":\"\",\"choices\":null},"
            + "{\"category\":\"Options\",\"name\":\"Odd\",\"displayName\":\"O\",\"description\":\"\",\"type\":\"script\",\"default\":\"\",\"choices\":null},"
            + "{\"category\":\"Options\",\"name\":null,\"displayName\":\"N\",\"description\":\"\",\"type\":\"int\",\"default\":\"\",\"choices\":null}"
            + "],\"lastUsed\":\"2026-11-13T12:00:00+00:00\"}}}";

        var defs = Cache().Get(A)!;

        Assert.Equal(["Follow", "Odd"], defs.Select(d => d.Name));
        Assert.Equal("other", defs[1].Type);
    }

    [Fact]
    public void The_books_have_names_for_the_forget_report()
    {
        Assert.Equal("Skua script preferences", Prefs().Name);
        Assert.Equal("Skua script options", Cache().Name);
    }
}
