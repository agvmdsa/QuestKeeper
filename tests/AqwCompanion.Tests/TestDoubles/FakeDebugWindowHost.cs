using AqwCompanion.App.Debug;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeDebugWindowHost : IDebugWindowHost
{
    public event EventHandler? Closed;

    public bool IsOpen { get; private set; }

    public int Shows { get; private set; }

    public void Show()
    {
        IsOpen = true;
        Shows++;
    }

    public void Close() => IsOpen = false;

    /// <summary>The user closes the window with its own close button.</summary>
    public void CloseByUser()
    {
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
