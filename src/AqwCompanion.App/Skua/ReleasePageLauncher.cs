using System.Diagnostics;
using Serilog;

namespace AqwCompanion.App.Skua;

/// <summary>Opens the release page of Questkeeper in the system browser. Only the exact address in <see cref="ReleasePage.Url"/> is ever launched.</summary>
public interface IReleasePageLauncher
{
    /// <summary>True when the URL is the release page and was handed to the system; false for any other text or a failed start. Never throws.</summary>
    bool Open(string url);
}

public static class ReleasePage
{
    public const string Url = "https://github.com/agvmdsa/QuestKeeper/releases/latest";
}

public sealed class ProcessReleasePageLauncher : IReleasePageLauncher
{
    private readonly Func<ProcessStartInfo, bool> _start;

    public ProcessReleasePageLauncher(Func<ProcessStartInfo, bool>? start = null) => _start = start ?? StartProcess;

    public bool Open(string url)
    {
        if (!string.Equals(url, ReleasePage.Url, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return _start(new ProcessStartInfo(ReleasePage.Url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Opening the release page failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private static bool StartProcess(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
        return true;
    }
}
