using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakePhoneHost : IPhoneHost
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
