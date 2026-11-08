using Avalonia.Controls;

namespace AqwCompanion.App.Shell;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Lets the tray decide about closing: the close button hides the window instead (the capture keeps running), while an
    /// application or OS shutdown closes it.
    /// </summary>
    public void KeepInTray(TrayController controller) =>
        Closing += (_, e) => e.Cancel = controller.ShouldCancelClose(e.CloseReason == WindowCloseReason.WindowClosing);
}
