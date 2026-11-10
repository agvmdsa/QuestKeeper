namespace AqwCompanion.App.Phone;

public enum TailscaleStateKind
{
    NotInstalled,
    NotSignedIn,
    HttpsNotEnabled,
    Ready,
    Error,
}

/// <summary>What Tailscale offers right now. <see cref="DnsName"/> is set only for Ready and has passed the name check; <see cref="Text"/> is the sentence Settings shows.</summary>
public sealed record TailscaleState(TailscaleStateKind Kind, string? DnsName, string Text);

/// <summary>The local Tailscale client: its state, and publishing the phone port to the tailnet over HTTPS. None of these throw.</summary>
public interface ITailscale
{
    Task<TailscaleState> GetStateAsync(CancellationToken ct);

    /// <summary>Publishes http://127.0.0.1:{port} to the tailnet (tailscale serve); false when it did not work.</summary>
    Task<bool> ServeAsync(int port, CancellationToken ct);

    /// <summary>Removes the published address (tailscale serve reset); false when it did not work.</summary>
    Task<bool> StopServeAsync(CancellationToken ct);
}

/// <summary>What a finished program left behind. <see cref="Truncated"/> means the output went over the limit and the program was stopped.</summary>
public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut, bool Truncated);

/// <summary>Runs one program with fixed arguments and captures its output within a time and size limit. Implementations must not throw for a program that fails to run.</summary>
public interface ITailscaleProcess
{
    Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, TimeSpan timeout, int maxOutputBytes, CancellationToken ct);
}
