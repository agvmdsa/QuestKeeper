using AqwCompanion.App.Account;
using Avalonia.Controls;

namespace AqwCompanion.App.Equipment;

/// <summary>The Equipped card; tapping it opens the details sidebar when there is equipment to show.</summary>
public partial class EquipmentCardView : UserControl
{
    public EquipmentCardView()
    {
        InitializeComponent();
        EquippedPanel.Tapped += (_, _) => CardTap.Open((DataContext as EquipmentCardViewModel)?.OpenDetailsCommand);
    }

    /// <summary>The control the sidebar returns focus to when it was not opened from a focused element.</summary>
    public Button OpenButton => ViewAllButton;
}
