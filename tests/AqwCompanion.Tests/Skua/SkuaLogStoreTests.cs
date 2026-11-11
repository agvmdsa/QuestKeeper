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
}
