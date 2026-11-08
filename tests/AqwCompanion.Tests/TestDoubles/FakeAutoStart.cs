using AqwCompanion.App.Infrastructure.AutoStart;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeAutoStart : IAutoStart
{
    public bool Enabled { get; set; }

    public bool Fails { get; set; }

    public bool IsSupported => true;

    public bool IsEnabled() => Enabled;

    public bool TrySet(bool enabled)
    {
        if (Fails)
        {
            return false;
        }

        Enabled = enabled;
        return true;
    }
}
