using AqwCompanion.App.Account;
using Avalonia.Controls;

namespace AqwCompanion.App.Badges;

/// <summary>The Badges card; tapping it opens the details sidebar when there are badges to show.</summary>
public partial class BadgesCardView : UserControl
{
    public BadgesCardView()
    {
        InitializeComponent();
        BadgesPanel.Tapped += (_, _) => CardTap.Open((DataContext as BadgesCardViewModel)?.OpenDetailsCommand);
    }

    /// <summary>The control the sidebar returns focus to when it was not opened from a focused element.</summary>
    public Button OpenButton => ViewAllBadgesButton;
}
