using AqwCompanion.App;
using AqwCompanion.App.Skua;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Tests.Skua;

public sealed class SkuaScriptFileTests : IDisposable
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
    public void The_files_live_in_the_data_root()
    {
        Assert.Equal(Path.Combine(_paths.Root, "skua-script-prefs.json"), _paths.SkuaScriptPrefsPath);
        Assert.Equal(Path.Combine(_paths.Root, "skua-script-options.json"), _paths.SkuaScriptOptionsPath);
    }

    [Fact]
    public void A_missing_file_reads_null_and_a_written_one_reads_back_without_a_temp_file()
    {
        var file = new SkuaScriptFile(_paths.SkuaScriptPrefsPath);
        Assert.Null(file.Read());

        file.Write("{\"version\":1}");

        Assert.Equal("{\"version\":1}", file.Read());
        Assert.False(File.Exists(_paths.SkuaScriptPrefsPath + ".tmp"));
    }

    [Fact]
    public void Delete_removes_the_file_and_its_temp_and_never_throws()
    {
        var file = new SkuaScriptFile(_paths.SkuaScriptOptionsPath);
        file.Delete();
        file.Write("x");
        File.WriteAllText(_paths.SkuaScriptOptionsPath + ".tmp", "y");

        file.Delete();

        Assert.False(File.Exists(_paths.SkuaScriptOptionsPath));
        Assert.False(File.Exists(_paths.SkuaScriptOptionsPath + ".tmp"));
    }

    [Fact]
    public void Prefs_persist_through_the_real_file()
    {
        var at = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        new SkuaScriptPrefs(new SkuaScriptFile(_paths.SkuaScriptPrefsPath), () => at).Set("0123456789abcdef", false);

        Assert.False(new SkuaScriptPrefs(new SkuaScriptFile(_paths.SkuaScriptPrefsPath), () => at).AskOptions("0123456789abcdef"));
    }
}
