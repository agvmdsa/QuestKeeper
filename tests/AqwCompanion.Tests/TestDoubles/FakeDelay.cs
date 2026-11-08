using AqwCompanion.App.Infrastructure;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Completes at once unless a test sets <see cref="Gate"/> to hold the delay open. Safe to read while a background task asks for delays.</summary>
public sealed class FakeDelay : IDelay
{
    private readonly object _lock = new();
    private readonly List<TimeSpan> _delays = [];

    /// <summary>A copy of the delays asked for so far, in order.</summary>
    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_lock)
            {
                return [.. _delays];
            }
        }
    }

    public TaskCompletionSource? Gate { get; set; }

    public CancellationToken LastToken { get; private set; }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _delays.Add(delay);
        }

        LastToken = ct;
        return Gate is null ? Task.CompletedTask : Gate.Task.WaitAsync(ct);
    }
}
