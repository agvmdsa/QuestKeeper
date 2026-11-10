using System.Diagnostics;
using System.Text;

namespace AqwCompanion.App.Phone;

/// <summary>
/// Runs one program with no shell and no window, from the Windows system folder, with the arguments passed as a list.
/// Stops it (and its children) when it runs past the time limit or prints more than the size limit. Tiny on purpose: only the Windows checklist covers it.
/// </summary>
public sealed class TailscaleProcess : ITailscaleProcess
{
    public async Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, TimeSpan timeout, int maxOutputBytes, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info);
        if (process is null)
        {
            return new ProcessResult(-1, "", false, false);
        }

        using var limit = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        var truncated = false;
        try
        {
            var stdout = ReadLimitedAsync(process.StandardOutput.BaseStream, maxOutputBytes, () => Kill(process), linked.Token);
            var stderr = ReadLimitedAsync(process.StandardError.BaseStream, maxOutputBytes, () => Kill(process), linked.Token);
            var read = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            truncated = read[0].Truncated || read[1].Truncated;
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, read[0].Text, false, truncated);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            return new ProcessResult(-1, "", true, truncated);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadLimitedAsync(Stream stream, int max, Action onOverflow, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var kept = new MemoryStream();
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0)
            {
                return (Encoding.UTF8.GetString(kept.ToArray()), false);
            }

            if (kept.Length + n > max)
            {
                // Stop the program now: the other pipe stays open until it exits.
                onOverflow();
                return ("", true);
            }

            kept.Write(buffer, 0, n);
        }
    }
}
