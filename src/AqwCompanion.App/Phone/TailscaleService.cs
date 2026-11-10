using System.Globalization;
using Serilog;

namespace AqwCompanion.App.Phone;

/// <summary>
/// Talks to the installed Tailscale client. Security: this app runs as administrator, so the program is only ever
/// <c>&lt;Program Files&gt;\Tailscale\tailscale.exe</c> (no PATH search, no configured path) and the arguments are fixed.
/// Nothing from Tailscale's output is logged or passed on, except a DNS name that passed the strict name check.
/// </summary>
public sealed class TailscaleService : ITailscale
{
    public const string NotInstalledText = "Tailscale is not installed. Install it on this PC and on your phone, then sign in with the same account.";
    public const string NotSignedInText = "Tailscale is installed but not signed in. Sign in to Tailscale on this PC.";
    public const string HttpsNotEnabledText = "In the Tailscale admin console, turn on MagicDNS and HTTPS certificates (one time), then try again.";
    public const string ReadyText = "Tailscale is ready.";
    public const string ErrorText = "Could not read the Tailscale state.";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public const int MaxOutputBytes = 256 * 1024;

    private static readonly string[] StatusArgs = ["status", "--json"];
    private static readonly string[] ServeOffArgs = ["serve", "reset"];

    private readonly ITailscaleProcess _process;
    private readonly string _exePath;
    private readonly Func<string, bool> _fileExists;

    public TailscaleService(ITailscaleProcess process, string? programFiles = null, Func<string, bool>? fileExists = null)
    {
        _process = process;
        _exePath = Path.Combine(programFiles ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        _fileExists = fileExists ?? File.Exists;
    }

    public async Task<TailscaleState> GetStateAsync(CancellationToken ct)
    {
        try
        {
            if (!_fileExists(_exePath))
            {
                return new TailscaleState(TailscaleStateKind.NotInstalled, null, NotInstalledText);
            }

            var result = await _process.RunAsync(_exePath, StatusArgs, Timeout, MaxOutputBytes, ct).ConfigureAwait(false);
            if (result.TimedOut || result.Truncated)
            {
                return new TailscaleState(TailscaleStateKind.Error, null, ErrorText);
            }

            // A stopped client may exit non-zero yet still print its state, so the output decides, not the exit code.
            return TailscaleStatusParser.Parse(result.Output);
        }
        catch (Exception ex)
        {
            Log.Warning("Tailscale status failed: {ExceptionType}", ex.GetType().Name);
            return new TailscaleState(TailscaleStateKind.Error, null, ErrorText);
        }
    }

    public Task<bool> ServeAsync(int port, CancellationToken ct)
    {
        if (port is < 1 or > 65535)
        {
            return Task.FromResult(false);
        }

        return RunSucceededAsync(["serve", "--bg", "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)], ct);
    }

    public Task<bool> StopServeAsync(CancellationToken ct) => RunSucceededAsync(ServeOffArgs, ct);

    private async Task<bool> RunSucceededAsync(string[] args, CancellationToken ct)
    {
        try
        {
            if (!_fileExists(_exePath))
            {
                return false;
            }

            var result = await _process.RunAsync(_exePath, args, Timeout, MaxOutputBytes, ct).ConfigureAwait(false);
            return !result.TimedOut && !result.Truncated && result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Warning("Tailscale serve command failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }
}
