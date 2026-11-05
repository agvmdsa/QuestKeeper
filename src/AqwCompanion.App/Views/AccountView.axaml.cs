using System.ComponentModel;
using AqwCompanion.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AqwCompanion.App.Views;

/// <summary>
/// The Account dashboard (cards) and its details sidebar. The embedded browser lives in the sign-in window, never in this tab.
/// The sidebar's open state lives in the view model; this code only does what XAML cannot: focus, tab isolation and
/// keeping the closed sidebar out of the way once its slide-out has finished.
/// </summary>
public partial class AccountView : UserControl
{
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(200);

    private AccountViewModel? _viewModel;
    private InputElement? _opener;

    public AccountView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as AccountViewModel);
        Scrim.Tapped += (_, _) => _viewModel?.CloseSidebarCommand.Execute(null);
        EquippedPanel.Tapped += (_, _) => _viewModel?.OpenEquippedDetailsCommand.Execute(null);
    }

    private void Attach(AccountViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplySidebar(viewModel?.IsSidebarOpen ?? false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.IsSidebarOpen) && _viewModel is { } viewModel)
        {
            ApplySidebar(viewModel.IsSidebarOpen);
        }
    }

    private void ApplySidebar(bool open)
    {
        if (open)
        {
            _opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
            if (_opener is null || ReferenceEquals(_opener, SidebarCloseButton) || Sidebar.IsVisualAncestorOf(_opener))
            {
                _opener = ViewAllButton;
            }

            // While the sidebar is open the cards can be neither clicked nor reached with Tab.
            Dashboard.IsHitTestVisible = false;
            KeyboardNavigation.SetTabNavigation(Dashboard, KeyboardNavigationMode.None);
            Sidebar.IsVisible = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_viewModel is not { IsSidebarOpen: true })
                    {
                        return;
                    }

                    Sidebar.Classes.Set("open", true);
                    SidebarCloseButton.Focus();
                },
                DispatcherPriority.Background);
            return;
        }

        var wasOpen = Sidebar.Classes.Contains("open");
        Sidebar.Classes.Set("open", false);
        Dashboard.IsHitTestVisible = true;
        KeyboardNavigation.SetTabNavigation(Dashboard, KeyboardNavigationMode.Continue);
        if (!wasOpen && !Sidebar.IsVisible)
        {
            return;
        }

        // Restore focus after the bindings have caught up (the opener may have been hidden by the same change).
        var opener = _opener;
        _opener = null;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_viewModel is { IsSidebarOpen: true })
                {
                    return;
                }

                var target = opener is { IsEffectivelyVisible: true, Focusable: true }
                    ? opener
                    : SignInButton.IsEffectivelyVisible ? SignInButton : RefreshButton;
                if (target.IsEffectivelyVisible)
                {
                    target.Focus();
                }
            },
            DispatcherPriority.Background);

        // Hide the closed sidebar after the slide-out so it is not exposed to Tab or screen readers.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (_viewModel is not { IsSidebarOpen: true })
                {
                    Sidebar.IsVisible = false;
                }
            },
            SlideDuration);
    }
}
