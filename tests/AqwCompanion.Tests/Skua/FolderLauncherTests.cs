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

        Assert.False(launcher.Open("https://github.com/agvmdsa/SkuaLink/releases/latest/"));
        Assert.False(launcher.Open("https://example.com"));
        Assert.False(launcher.Open(""));
        Assert.True(launcher.Open(ReleasePage.Url));

        Assert.Equal(["https://github.com/agvmdsa/SkuaLink/releases/latest"], urls);
    }

    [Fact]
    public void The_release_launcher_does_not_throw_when_the_start_fails()
    {
        var launcher = new ProcessReleasePageLauncher(_ => throw new InvalidOperationException("no"));

        Assert.False(launcher.Open(ReleasePage.Url));
    }

    [Fact]
    public void The_folder_launcher_counts_a_start_only_when_a_process_came_back()
    {
        var info = new ProcessStartInfo("explorer.exe");

        Assert.False(ProcessFolderLauncher.StartProcess(info, _ => null));
        Assert.True(ProcessFolderLauncher.StartProcess(info, _ => Process.GetCurrentProcess()));
    }

    [Fact]
    public void The_release_launcher_counts_a_shell_start_without_a_new_process_as_opened()
    {
        // ShellExecuteEx returns no process when a running browser takes the URL: Process.Start answers null on success.
        var info = new ProcessStartInfo(ReleasePage.Url) { UseShellExecute = true };

        Assert.True(ProcessReleasePageLauncher.StartProcess(info, _ => null));
        Assert.True(ProcessReleasePageLauncher.StartProcess(info, _ => Process.GetCurrentProcess()));
        Assert.Throws<InvalidOperationException>(() => ProcessReleasePageLauncher.StartProcess(info, _ => throw new InvalidOperationException("no")));
    }
}
