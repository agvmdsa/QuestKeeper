using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeTailscale : ITailscale
{
    public const string Dns = "my-pc.tail1234.ts.net";

    public TailscaleState State { get; set; } = Ready();

    public bool ServeResult { get; set; } = true;

    public Exception? Throws { get; set; }

    public Func<bool>? HostRunning { get; set; }

    /// <summary>When set, ServeAsync completes only when this does (an in-flight serve).</summary>
    public TaskCompletionSource<bool>? ServeGate { get; set; }

    /// <summary>When set, StopServeAsync completes only when this does.</summary>
    public TaskCompletionSource<bool>? StopGate { get; set; }

    public bool StopResult { get; set; } = true;

    /// <summary>Runs while ServeAsync is in flight, before it completes.</summary>
    public Action? OnServe { get; set; }

    public int StateCalls { get; private set; }

    public List<int> Serves { get; } = [];

    public int Stops { get; private set; }

    public List<bool> HostRunningAtServe { get; } = [];

    public static TailscaleState Ready(string dns = Dns) => new(TailscaleStateKind.Ready, dns, TailscaleService.ReadyText);

    public Task<TailscaleState> GetStateAsync(CancellationToken ct)
    {
        StateCalls++;
        return Throws is null ? Task.FromResult(State) : Task.FromException<TailscaleState>(Throws);
    }

    public Task<bool> ServeAsync(int port, CancellationToken ct)
    {
        Serves.Add(port);
        HostRunningAtServe.Add(HostRunning?.Invoke() ?? false);
        OnServe?.Invoke();
        if (ServeGate is { } gate)
        {
            return gate.Task;
        }

        return Throws is null ? Task.FromResult(ServeResult) : Task.FromException<bool>(Throws);
    }

    public Task<bool> StopServeAsync(CancellationToken ct)
    {
        Stops++;
        if (StopGate is { } gate)
        {
            return gate.Task;
        }

        return Throws is null ? Task.FromResult(StopResult) : Task.FromException<bool>(Throws);
    }
}
