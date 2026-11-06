using System.Diagnostics;
using AqwCompanion.Core.Wiki;
using Serilog;

namespace AqwCompanion.App.HardFarms;

/// <summary>Opens a page of the wiki in the system browser. Only addresses of exactly the shape https://aqwwiki.wikidot.com/&lt;valid page name&gt; are ever launched.</summary>
public interface IUrlLauncher
{
    /// <summary>True when the URL was handed to the system; false when it is not a wiki URL or the launch failed.</summary>
    bool Open(string url);
}

/// <summary>Hands the URL to the operating system's default browser (ShellExecute on Windows, `open` on macOS).</summary>
public sealed class ProcessUrlLauncher : IUrlLauncher
{
    public bool Open(string url)
    {
        if (!WikiUrls.IsSafePageUrl(url))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Opening a wiki page failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }
}
