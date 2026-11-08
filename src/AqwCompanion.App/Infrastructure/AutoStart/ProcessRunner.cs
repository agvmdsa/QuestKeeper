using System.Diagnostics;

namespace AqwCompanion.App.Infrastructure.AutoStart;

/// <summary>Runs a program without a window and waits for it a short while. Tiny on purpose: only the Windows checklist covers it.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    public int Run(string file, IReadOnlyList<string> args)
    {
        try
        {
            var info = new ProcessStartInfo(file)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return -1;
            }

            // The output is not used, but an unread pipe could fill up and block the program.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (process.WaitForExit(Limit))
            {
                return process.ExitCode;
            }

            process.Kill();
            return -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
