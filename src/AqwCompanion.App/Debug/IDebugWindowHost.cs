namespace AqwCompanion.App.Debug;

/// <summary>The debug window as the debug mode sees it. <see cref="Close"/> and <see cref="Show"/> may be called when already closed or open.</summary>
public interface IDebugWindowHost
{
    /// <summary>Raised when the user closed the window with its own close button.</summary>
    event EventHandler? Closed;

    void Show();

    void Close();
}
