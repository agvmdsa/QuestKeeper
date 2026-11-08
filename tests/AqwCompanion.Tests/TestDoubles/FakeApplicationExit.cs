using AqwCompanion.App.GameData;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeApplicationExit : IApplicationExit
{
    public int ExitCalls { get; private set; }

    public void Exit() => ExitCalls++;
}
