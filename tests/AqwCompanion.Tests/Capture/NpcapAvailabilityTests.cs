using AqwCompanion.Capture;

namespace AqwCompanion.Tests.Capture;

public class NpcapAvailabilityTests
{
    private const string System32 = @"C:\Windows\System32";

    private static NpcapStatus Check(bool isWindows, params string[] existing) =>
        NpcapAvailability.Check(existing.Contains, () => System32, isWindows);

    [Fact]
    public void Not_windows_is_not_supported_even_if_files_exist()
    {
        Assert.Equal(NpcapStatus.NotSupported, Check(false, Path.Combine(System32, "Npcap", "wpcap.dll")));
    }

    [Fact]
    public void Npcap_folder_dll_means_available()
    {
        Assert.Equal(NpcapStatus.Available, Check(true, Path.Combine(System32, "Npcap", "wpcap.dll")));
    }

    [Fact]
    public void Legacy_winpcap_path_means_available()
    {
        Assert.Equal(NpcapStatus.Available, Check(true, Path.Combine(System32, "wpcap.dll")));
    }

    [Fact]
    public void Neither_file_means_missing()
    {
        Assert.Equal(NpcapStatus.Missing, Check(true));
    }

    [Fact]
    public void Download_url_is_the_official_site()
    {
        Assert.Equal("https://npcap.com/", NpcapAvailability.DownloadUrl);
    }
}
