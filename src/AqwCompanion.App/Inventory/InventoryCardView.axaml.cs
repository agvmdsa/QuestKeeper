using AqwCompanion.App.Account;
using Avalonia.Controls;

namespace AqwCompanion.App.Inventory;

/// <summary>The Inventory card; tapping it opens the details sidebar when there are items to show.</summary>
public partial class InventoryCardView : UserControl
{
    public InventoryCardView()
    {
        InitializeComponent();
        InventoryPanel.Tapped += (_, _) => CardTap.Open((DataContext as InventoryCardViewModel)?.OpenDetailsCommand);
    }

    /// <summary>The control the sidebar returns focus to when it was not opened from a focused element.</summary>
    public Button OpenButton => ViewAllInventoryButton;
}
