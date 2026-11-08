namespace AqwCompanion.Tests.Architecture;

/// <summary>
/// The installer cannot be built or run here (Inno Setup is Windows-only and runs in the release workflow), so these tests guard
/// the rules it must keep: Npcap is never bundled, the page for it exists, and the files it names are in the repository.
/// </summary>
public class InstallerScriptTests
{
    private static string Script() => File.ReadAllText(Path.Combine(RepositoryRoot(), "installer", "Questkeeper.iss"));

    [Fact]
    public void The_installer_links_to_the_official_npcap_page_and_never_names_an_npcap_file_to_ship()
    {
        var script = Script();

        Assert.Contains("https://npcap.com/", script);
        // Only [Files] lines can ship something; none of them may mention Npcap.
        var files = script.Split("[Files]")[1].Split("\n[")[0];
        Assert.DoesNotContain("npcap", files, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_installer_has_a_page_that_checks_for_npcap_the_way_the_app_does()
    {
        var script = Script();

        Assert.Contains("CreateCustomPage", script);
        Assert.Contains(@"{sys}\Npcap\wpcap.dll", script);
        Assert.Contains(@"{sys}\wpcap.dll", script);
    }

    [Fact]
    public void The_installer_asks_for_administrator_rights_like_the_app()
    {
        Assert.Contains("PrivilegesRequired=admin", Script());
    }

    [Fact]
    public void The_start_after_install_entry_uses_shellexec_so_the_administrator_prompt_can_show()
    {
        var launch = Script().Split('\n').Single(line => line.Contains("Description: \"Start ", StringComparison.Ordinal));

        Assert.Contains("shellexec", launch);
        Assert.Contains("postinstall", launch);
    }

    [Fact]
    public void The_installer_removes_the_start_with_windows_task_and_never_deletes_data_without_asking()
    {
        var script = Script();

        Assert.Contains("schtasks.exe", script);
        Assert.Contains("MsgBox", script);
        Assert.Contains("UninstallSilent", script);
    }

    [Fact]
    public void The_installer_icon_and_the_data_folder_name_match_the_app()
    {
        var script = Script();

        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "src", "AqwCompanion.App", "Assets", "app.ico")));
        Assert.Contains(@"SetupIconFile=..\src\AqwCompanion.App\Assets\app.ico", script);
        Assert.Contains("#define MyDataFolder \"AqwCompanion\"", script);
    }

    [Fact]
    public void The_release_workflow_builds_the_installer_and_lists_it_in_the_checksums()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("installer/Questkeeper.iss", workflow);
        Assert.Contains("Questkeeper-Setup-v", workflow);
        Assert.Contains("SHA256SUMS.txt", workflow);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "installer", "Questkeeper.iss")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("installer/Questkeeper.iss not found above " + AppContext.BaseDirectory);
    }
}
