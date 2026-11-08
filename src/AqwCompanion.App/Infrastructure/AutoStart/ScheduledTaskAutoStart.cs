using AqwCompanion.App.Infrastructure.Elevation;

namespace AqwCompanion.App.Infrastructure.AutoStart;

/// <summary>
/// Starts the app at sign-in through a Windows scheduled task. A Run-key entry cannot work here: the app always asks for
/// administrator rights, and Windows does not start such a program from the Run key. A task with the highest privileges does,
/// and the app (already elevated) can create it without another prompt. The task starts the app with <c>--background</c> so it
/// waits in the notification area instead of opening its window.
/// </summary>
public sealed class ScheduledTaskAutoStart(IProcessRunner runner, string? exePath, string? userName, Func<bool> isWindows) : IAutoStart
{
    public const string TaskName = "AQW Companion";

    private const string Schtasks = "schtasks.exe";

    public bool IsSupported => isWindows() && !string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(userName);

    public bool IsEnabled() => IsSupported && runner.Run(Schtasks, ["/Query", "/TN", TaskName]) == 0;

    public bool TrySet(bool enabled)
    {
        if (!IsSupported)
        {
            return false;
        }

        if (enabled)
        {
            return runner.Run(
                Schtasks,
                [
                    "/Create", "/TN", TaskName, "/TR", "\"" + exePath + "\" " + StartupArguments.BackgroundOption, "/SC", "ONLOGON",
                    "/RL", "HIGHEST", "/RU", userName!, "/IT", "/DELAY", "0000:15", "/F",
                ]) == 0;
        }

        // Nothing to remove is the same as removed.
        return !IsEnabled() || runner.Run(Schtasks, ["/Delete", "/TN", TaskName, "/F"]) == 0;
    }
}
