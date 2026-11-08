using AqwCompanion.App;
using AqwCompanion.App.Settings;

namespace AqwCompanion.Tests.Settings;

public sealed class JsonAppSettingsStoreTests : IDisposable
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
    public void Without_a_file_the_defaults_apply_and_nothing_is_created()
    {
        var settings = new JsonAppSettingsStore(_paths.SettingsPath).Load();

        Assert.False(settings.DebugMode);
        Assert.False(Directory.Exists(_paths.Root));
    }

    [Fact]
    public void A_saved_value_is_read_back_by_a_new_store()
    {
        new JsonAppSettingsStore(_paths.SettingsPath).Save(new AppSettings(DebugMode: true));

        Assert.True(new JsonAppSettingsStore(_paths.SettingsPath).Load().DebugMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void A_file_that_cannot_be_read_gives_the_defaults(string content)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.SettingsPath, content);

        Assert.False(new JsonAppSettingsStore(_paths.SettingsPath).Load().DebugMode);
    }

    [Fact]
    public void Saving_never_throws_when_the_folder_cannot_be_written()
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(Path.Combine(_paths.Root, "blocker"), "x");
        var store = new JsonAppSettingsStore(Path.Combine(_paths.Root, "blocker", "settings.json"));

        var ex = Record.Exception(() => store.Save(new AppSettings(true)));

        Assert.Null(ex);
    }

    [Fact]
    public void A_successful_save_leaves_no_temp_file()
    {
        new JsonAppSettingsStore(_paths.SettingsPath).Save(new AppSettings(true));

        Assert.Equal(["settings.json"], Directory.GetFiles(_paths.Root).Select(Path.GetFileName));
    }
}
