using AqwCompanion.App.Shell;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Counts the Show and Hide calls and also writes them to the log list when one is given.</summary>
public sealed class FakeMainWindowHost(List<string>? log = null) : IMainWindowHost
{
    public int ShowCalls { get; private set; }

    public int HideCalls { get; private set; }

    public void Show()
    {
        ShowCalls++;
        log?.Add("show");
    }

    public void Hide()
    {
        HideCalls++;
        log?.Add("hide");
    }
}
