using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AqwCompanion.App.Reputation;

/// <summary>The Reputation tab: the import and refresh buttons, the messages and the list of faction standings. Reads the game client's file when the tab opens.</summary>
public partial class ReputationView : UserControl
{
    public ReputationView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshWhenVisible();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == DataContextProperty)
        {
            RefreshWhenVisible();
        }
    }

    // The view model ignores a refresh that is already running, so a second trigger for the same opening does no harm.
    private void RefreshWhenVisible()
    {
        if (((Visual)this).GetVisualRoot() is not null && IsEffectivelyVisible && DataContext is ReputationViewModel viewModel)
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }
}
