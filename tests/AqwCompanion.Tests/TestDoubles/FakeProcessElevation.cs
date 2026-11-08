using AqwCompanion.App.Infrastructure.Elevation;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeProcessElevation : IProcessElevation
{
    public bool IsElevated { get; set; } = true;

    public bool RelaunchResult { get; set; }

    public List<string> RelaunchedWith { get; } = [];

    public bool TryRelaunchElevated(string dataRoot)
    {
        RelaunchedWith.Add(dataRoot);
        return RelaunchResult;
    }
}
