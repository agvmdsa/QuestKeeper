using AqwCompanion.App.SignIn;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Completes at once unless a test sets <see cref="Gate"/> to hold the delay open.</summary>
public sealed class FakeDelay : IDelay
{
    public List<TimeSpan> Delays { get; } = [];

    public TaskCompletionSource? Gate { get; set; }

    public CancellationToken LastToken { get; private set; }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
    {
        Delays.Add(delay);
        LastToken = ct;
        return Gate is null ? Task.CompletedTask : Gate.Task.WaitAsync(ct);
    }
}
