using Avalonia.Controls;

namespace AqwCompanion.App.Account;

/// <summary>
/// The Account dashboard: the connection card, the feature cards and the details sidebar over them. The embedded
/// browser lives in the sign-in window, never in this tab. The sidebar view keeps its own focus handling; this view
/// only tells it what it covers and where focus goes when the opener is not available.
/// </summary>
public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
        SidebarHost.Underlay = Dashboard;
        SidebarHost.DefaultOpener = content => content is Badges.BadgesCardViewModel ? BadgesCard.OpenButton : EquipmentCard.OpenButton;
        SidebarHost.FallbackFocus = () => SignInButton.IsEffectivelyVisible ? SignInButton : RefreshButton;
    }
}
