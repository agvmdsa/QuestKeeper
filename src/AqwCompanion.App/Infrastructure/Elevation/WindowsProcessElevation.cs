using System.Diagnostics;
using System.Security.Principal;

namespace AqwCompanion.App.Infrastructure.Elevation;

/// <summary>
/// Elevation through the Windows administrator prompt. The Windows calls cannot be unit-tested on other systems,
/// so this class stays tiny and is covered by the manual Windows checklist.
/// </summary>
public sealed class WindowsProcessElevation : IProcessElevation
{
    public bool IsElevated =>
        OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public bool TryRelaunchElevated(string dataRoot)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        try
        {
            using var started = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = StartupArguments.ForRelaunch(dataRoot),
            });
            return started is not null;
        }
        catch (Exception)
        {
            // Win32Exception 1223 means the prompt was declined; any other failure means the same to the caller.
            return false;
        }
    }
}
