using AqwCompanion.App;
using AqwCompanion.App.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public sealed class SkuaLogStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_loads_empty()
    {
        Assert.Empty(new SkuaLogStore(_paths.SkuaLogPath).Load());
    }

    [Fact]
    public void The_default_path_is_skua_log_json()
    {
        Assert.Equal(Path.Combine(_paths.Root, "skua-log.json"), _paths.SkuaLogPath);
    }

    [Fact]
    public void Saved_entries_round_trip()
    {
        var log = new List<SkuaLogEntry>
        {
            new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), SkuaLogKind.QuestTurnedIn, "Turned in \"Q\""),
            new(new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero), SkuaLogKind.ScriptError, "x"),
        };
        new SkuaLogStore(_paths.SkuaLogPath).Save(log);

        Assert.Equal(log, new SkuaLogStore(_paths.SkuaLogPath).Load());
    }

    [Fact]
    public void A_merged_drop_round_trips_its_item_and_count()
    {
        var log = new List<SkuaLogEntry>
        {
            new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), SkuaLogKind.Dropped, "Drop: Blood Orb x13", "Blood Orb", 13),
            new(new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero), SkuaLogKind.Died, "Died"),
        };
        new SkuaLogStore(_paths.SkuaLogPath).Save(log);

        Assert.Equal(log, new SkuaLogStore(_paths.SkuaLogPath).Load());
        Assert.Contains("\"item\":\"Blood Orb\",\"count\":13", File.ReadAllText(_paths.SkuaLogPath));
    }

    [Fact]
    public void A_file_from_before_item_and_count_loads_with_none()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SkuaLogPath, """[{"at":"2026-01-01T00:00:00+00:00","kind":"dropped","text":"Drop: Blood Orb"}]""");

        var entry = Assert.Single(new SkuaLogStore(_paths.SkuaLogPath).Load());

        Assert.Equal(new SkuaLogEntry(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), SkuaLogKind.Dropped, "Drop: Blood Orb"), entry);
    }

    [Theory]
    [InlineData("\"item\":\"X\",\"count\":0", "X", 1)]
    [InlineData("\"item\":\"X\",\"count\":-4", "X", 1)]
    [InlineData("\"item\":\"X\",\"count\":100000", "X", 99999)]
    [InlineData("\"item\":null,\"count\":9", null, 1)]
    [InlineData("\"count\":9", null, 1)]
    public void A_stored_count_is_kept_within_1_to_99999_and_needs_an_item(string fields, string? item, int count)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SkuaLogPath, "[{\"at\":\"2026-01-01T00:00:00+00:00\",\"kind\":\"dropped\",\"text\":\"Drop: X\"," + fields + "}]");

        var entry = Assert.Single(new SkuaLogStore(_paths.SkuaLogPath).Load());

        Assert.Equal(item, entry.Item);
        Assert.Equal(count, entry.Count);
    }

    [Fact]
    public void A_stored_item_is_cut_to_the_text_cap()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(
            _paths.SkuaLogPath,
            "[{\"at\":\"2026-01-01T00:00:00+00:00\",\"kind\":\"dropped\",\"text\":\"Drop: X\",\"item\":\"" + new string('i', 400) + "\",\"count\":2}]");

        Assert.Equal(SkuaProtocol.MaxTextLength, Assert.Single(new SkuaLogStore(_paths.SkuaLogPath).Load()).Item!.Length);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{}")]
    public void A_corrupt_file_loads_empty(string content)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SkuaLogPath, content);

        Assert.Empty(new SkuaLogStore(_paths.SkuaLogPath).Load());
    }

    [Fact]
    public void An_entry_with_an_unknown_kind_is_skipped_and_the_rest_kept()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(
            _paths.SkuaLogPath,
            """[{"at":"2026-01-01T00:00:00+00:00","kind":"teleported","text":"bad"},{"at":"2026-01-01T00:00:00+00:00","kind":"died","text":"ok"},{"at":"2026-01-01T00:00:00+00:00","kind":7,"text":"num"}]""");

        var loaded = new SkuaLogStore(_paths.SkuaLogPath).Load();

        Assert.Equal(["ok"], loaded.Select(e => e.Text));
    }

    [Fact]
    public void Delete_removes_the_file_and_the_temp_file()
    {
        var store = new SkuaLogStore(_paths.SkuaLogPath);
        store.Save([new SkuaLogEntry(DateTimeOffset.UnixEpoch, SkuaLogKind.Died, "x")]);
        File.WriteAllText(_paths.SkuaLogPath + ".tmp", "x");

        store.Delete();

        Assert.False(File.Exists(_paths.SkuaLogPath));
        Assert.False(File.Exists(_paths.SkuaLogPath + ".tmp"));
    }

    [Fact]
    public void Saving_and_deleting_never_throw_when_the_folder_cannot_be_written()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(Path.Combine(_paths.Root, "blocker"), "x");
        var store = new SkuaLogStore(Path.Combine(_paths.Root, "blocker", "skua-log.json"));

        Assert.Null(Record.Exception(() => store.Save([new SkuaLogEntry(DateTimeOffset.UnixEpoch, SkuaLogKind.Died, "x")])));
        Assert.Null(Record.Exception(store.Delete));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void A_path_the_platform_rejects_never_throws()
    {
        var store = new SkuaLogStore("bad\0path/skua-log.json");

        Assert.Null(Record.Exception(store.Load));
        Assert.Null(Record.Exception(() => store.Save([new SkuaLogEntry(DateTimeOffset.UnixEpoch, SkuaLogKind.Died, "x")])));
        Assert.Null(Record.Exception(store.Delete));
    }

    [Fact]
    public void Each_character_has_its_own_file_and_the_legacy_store_is_skua_log_json()
    {
        var stores = new SkuaLogStores(_paths.Root);
        var entry = new SkuaLogEntry(DateTimeOffset.UnixEpoch, SkuaLogKind.Sold, "sold");

        stores.ForCharacter("Hero").Save([entry]);
        stores.Legacy.Save([entry with { Text = "legacy" }]);

        Assert.True(File.Exists(Path.Combine(_paths.Root, "skua-log.hero.json")));
        Assert.Equal(["sold"], stores.ForCharacter("hero").Load().Select(e => e.Text));
        Assert.Empty(stores.ForCharacter("Zed").Load());
        Assert.Equal(["legacy"], new SkuaLogStore(_paths.SkuaLogPath).Load().Select(e => e.Text));
    }

    [Fact]
    public void Delete_all_removes_every_skua_log_and_nothing_else()
    {
        var stores = new SkuaLogStores(_paths.Root);
        var entry = new SkuaLogEntry(DateTimeOffset.UnixEpoch, SkuaLogKind.Sold, "sold");
        stores.ForCharacter("Hero").Save([entry]);
        stores.ForCharacter("Dark Lord").Save([entry]);
        stores.Legacy.Save([entry]);
        File.WriteAllText(Path.Combine(_paths.Root, "skua-log.zed.json.tmp"), "x");
        File.WriteAllText(Path.Combine(_paths.Root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_paths.Root, "skua-script-prefs.json"), "{}");

        stores.DeleteAll();

        Assert.Equal(["settings.json", "skua-script-prefs.json"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Delete_all_without_a_folder_does_nothing()
    {
        new SkuaLogStores(Path.Combine(_paths.Root, "missing")).DeleteAll();
    }
}
