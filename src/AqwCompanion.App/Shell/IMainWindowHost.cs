namespace AqwCompanion.App.Shell;

/// <summary>The main window as the tray sees it.</summary>
public interface IMainWindowHost
{
    /// <summary>Shows the window, restores it when minimized and brings it to the front.</summary>
    void Show();

    /// <summary>Hides the window without closing it.</summary>
    void Hide();
}
