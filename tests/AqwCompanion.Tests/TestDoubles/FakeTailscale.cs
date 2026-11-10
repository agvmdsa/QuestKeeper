using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeTailscale : ITailscale
{
    public const string Dns = "my-pc.tail1234.ts.net";

    public TailscaleState State { get; set; } = Ready();

    public bool ServeResult { get; set; } = true;

    public Exception? Throws { get; set; }

    public Func<bool>? HostRunning { get; set; }

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
        return Throws is null ? Task.FromResult(ServeResult) : Task.FromException<bool>(Throws);
    }

    public Task<bool> StopServeAsync(CancellationToken ct)
    {
        Stops++;
        return Throws is null ? Task.FromResult(true) : Task.FromException<bool>(Throws);
    }
}
