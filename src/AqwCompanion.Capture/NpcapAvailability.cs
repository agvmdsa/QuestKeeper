namespace AqwCompanion.Capture;

/// <summary>Whether the Npcap driver the live capture needs is installed.</summary>
public enum NpcapStatus
{
    Available,
    Missing,

    /// <summary>Not Windows.</summary>
    NotSupported,
}

/// <summary>Detects Npcap by looking for its library on disk; never loads it.</summary>
public static class NpcapAvailability
{
    public const string DownloadUrl = "https://npcap.com/";

    /// <summary>Reads the file system only.</summary>
    public static NpcapStatus Check() =>
        Check(File.Exists, () => Environment.GetFolderPath(Environment.SpecialFolder.System), OperatingSystem.IsWindows());

    public static NpcapStatus Check(Func<string, bool> fileExists, Func<string> systemDirectory, bool isWindows)
    {
        if (!isWindows)
        {
            return NpcapStatus.NotSupported;
        }

        var system = systemDirectory();
        return fileExists(Path.Combine(system, "Npcap", "wpcap.dll")) || fileExists(Path.Combine(system, "wpcap.dll"))
            ? NpcapStatus.Available
            : NpcapStatus.Missing;
    }
}
