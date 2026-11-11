using System.Diagnostics;
using AqwCompanion.App.Skua;

namespace AqwCompanion.Tests.Skua;

public class FolderLauncherTests
{
    [Fact]
    public void A_missing_directory_returns_false_and_starts_nothing()
    {
        var started = 0;
        var launcher = new ProcessFolderLauncher(_ => { started++; return true; }, _ => false);

        Assert.False(launcher.Open("/nowhere/plugins"));
        Assert.False(launcher.Open(""));
        Assert.Equal(0, started);
    }

    [Fact]
    public void An_existing_directory_is_passed_as_one_argument_without_a_shell()
    {
        ProcessStartInfo? seen = null;
        var launcher = new ProcessFolderLauncher(info => { seen = info; return true; }, _ => true);
        const string Path = "/a b/plugins; rm -rf";

        Assert.True(launcher.Open(Path));

        Assert.NotNull(seen);
        Assert.False(seen.UseShellExecute);
        Assert.Equal([Path], seen.ArgumentList);
    }

    [Fact]
    public void A_failing_start_returns_false_without_throwing()
    {
        var launcher = new ProcessFolderLauncher(_ => throw new InvalidOperationException("no"), _ => true);

        Assert.False(launcher.Open("/x"));
    }

    [Fact]
    public void The_release_launcher_opens_only_the_exact_release_url()
    {
        var urls = new List<string>();
        var launcher = new ProcessReleasePageLauncher(info => { urls.Add(info.FileName); return true; });

        Assert.False(launcher.Open("https://github.com/agvmdsa/QuestKeeper/releases/latest/"));
        Assert.False(launcher.Open("https://example.com"));
        Assert.False(launcher.Open(""));
        Assert.True(launcher.Open(ReleasePage.Url));

        Assert.Equal(["https://github.com/agvmdsa/QuestKeeper/releases/latest"], urls);
    }

    [Fact]
    public void The_release_launcher_does_not_throw_when_the_start_fails()
    {
        var launcher = new ProcessReleasePageLauncher(_ => throw new InvalidOperationException("no"));

        Assert.False(launcher.Open(ReleasePage.Url));
    }
}
