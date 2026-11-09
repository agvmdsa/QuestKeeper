using AqwCompanion.App.Feed;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeLocalFeedHost : ILocalFeedHost
{
    public bool IsRunning { get; private set; }

    public bool CanStart { get; set; } = true;

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public bool TryStart()
    {
        Starts++;
        IsRunning = CanStart;
        return CanStart;
    }

    public void Stop()
    {
        Stops++;
        IsRunning = false;
    }
}
