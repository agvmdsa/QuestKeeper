using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeTailscaleProcess : ITailscaleProcess
{
    public List<(string Exe, string[] Args, TimeSpan Timeout, int MaxBytes)> Calls { get; } = [];

    public ProcessResult Result { get; set; } = new(0, "", false, false);

    public Exception? Throws { get; set; }

    public Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, TimeSpan timeout, int maxOutputBytes, CancellationToken ct)
    {
        Calls.Add((exePath, args.ToArray(), timeout, maxOutputBytes));
        if (Throws is not null)
        {
            throw Throws;
        }

        return Task.FromResult(Result);
    }
}
