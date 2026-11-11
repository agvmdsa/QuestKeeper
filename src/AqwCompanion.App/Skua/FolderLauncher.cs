using System.Diagnostics;
using Serilog;

namespace AqwCompanion.App.Skua;

/// <summary>Shows a folder in the system file manager.</summary>
public interface IFolderLauncher
{
    /// <summary>True when the folder exists and was handed to the system; false for a missing path or a failed start. Never throws.</summary>
    bool Open(string path);
}

/// <summary>Opens an existing directory with explorer.exe on Windows, `open` on macOS and xdg-open elsewhere. The path is one argument, never part of a shell string, and nothing is created.</summary>
public sealed class ProcessFolderLauncher : IFolderLauncher
{
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly Func<string, bool> _directoryExists;

    public ProcessFolderLauncher(Func<ProcessStartInfo, bool>? start = null, Func<string, bool>? directoryExists = null)
    {
        _start = start ?? StartProcess;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public bool Open(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !_directoryExists(path))
            {
                return false;
            }

            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open")
            {
                UseShellExecute = false,
            };
            info.ArgumentList.Add(path);
            return _start(info);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Opening a folder failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private static bool StartProcess(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
        return true;
    }
}
