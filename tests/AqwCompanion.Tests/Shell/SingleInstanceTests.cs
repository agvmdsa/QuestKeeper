using AqwCompanion.App.Infrastructure;

namespace AqwCompanion.Tests.Shell;

public class SingleInstanceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static string UniqueName() => "AqwCompanionTest" + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_first_becomes_primary()
    {
        using var first = new SingleInstance(UniqueName());

        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));
    }

    [Fact]
    public void A_second_instance_with_the_same_name_does_not()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));

        Assert.False(second.TryBecomePrimary(TimeSpan.Zero));
    }

    [Fact]
    public void Another_name_is_independent()
    {
        using var first = new SingleInstance(UniqueName());
        using var other = new SingleInstance(UniqueName());
        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));

        Assert.True(other.TryBecomePrimary(TimeSpan.Zero));
    }

    [Fact]
    public async Task After_the_first_is_disposed_the_second_can_become_primary_within_the_wait()
    {
        var name = UniqueName();
        var first = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            first.Dispose();
        });

        var became = second.TryBecomePrimary(Wait);

        Assert.True(became);
        await release.WaitAsync(Wait);
    }

    [Fact]
    public void Becoming_primary_twice_answers_true_again()
    {
        using var first = new SingleInstance(UniqueName());

        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));
        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));
    }

    [Fact]
    public async Task A_signal_runs_the_primary_callback_once_per_signal()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Named events exist only on Windows; elsewhere the guard degrades to no signal.
            return;
        }

        var name = UniqueName();
        using var primary = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(primary.TryBecomePrimary(TimeSpan.Zero));
        using var calls = new SemaphoreSlim(0);
        primary.ListenForSignals(() => calls.Release());

        second.SignalPrimary();
        Assert.True(await calls.WaitAsync(Wait));
        second.SignalPrimary();
        Assert.True(await calls.WaitAsync(Wait));

        Assert.Equal(0, calls.CurrentCount);
    }

    [Fact]
    public async Task Dispose_stops_the_listening()
    {
        var name = UniqueName();
        var primary = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(primary.TryBecomePrimary(TimeSpan.Zero));
        var count = 0;
        primary.ListenForSignals(() => Interlocked.Increment(ref count));

        primary.Dispose();
        second.SignalPrimary();
        await Task.Delay(200);

        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public void Disposing_twice_is_safe()
    {
        var instance = new SingleInstance(UniqueName());
        instance.TryBecomePrimary(TimeSpan.Zero);
        instance.ListenForSignals(() => { });

        instance.Dispose();
        instance.Dispose();
    }

    [Fact]
    public void Signalling_after_dispose_does_not_throw()
    {
        var instance = new SingleInstance(UniqueName());
        instance.Dispose();

        instance.SignalPrimary();
        Assert.False(instance.TryBecomePrimary(TimeSpan.Zero));
    }

    [Fact]
    public void A_guard_that_is_not_supported_always_answers_primary_and_never_throws()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name, supported: false);
        using var second = new SingleInstance(name, supported: false);

        Assert.True(first.TryBecomePrimary(TimeSpan.Zero));
        Assert.True(second.TryBecomePrimary(TimeSpan.Zero));
        first.SignalPrimary();
        first.ListenForSignals(() => throw new InvalidOperationException());
    }
}
