namespace AqwCompanion.App.Infrastructure;

/// <summary>Waiting seam so the short "Connected" pause does not slow tests down.</summary>
public interface IDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct = default);
}

public sealed class TaskDelay : IDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default) => Task.Delay(delay, ct);
}
