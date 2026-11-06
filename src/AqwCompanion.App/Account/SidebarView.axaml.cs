using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AqwCompanion.App.Account;

/// <summary>
/// The details sidebar overlay. Its open state lives in the view model; this code only does what XAML cannot: focus,
/// tab isolation of the content underneath and keeping the closed sidebar out of the way once its slide-out has finished.
/// The host supplies what the sidebar cannot know: the content it covers, where focus goes when it did not open from a
/// focused element, and where focus goes when that opener is gone.
/// </summary>
public partial class SidebarView : UserControl
{
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(200);

    private SidebarViewModel? _viewModel;
    private InputElement? _opener;

    public SidebarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as SidebarViewModel);
        Scrim.Tapped += (_, _) => _viewModel?.CloseCommand.Execute(null);
    }

    /// <summary>The content the sidebar covers; it is neither clickable nor reachable with Tab while the sidebar is open.</summary>
    public Control? Underlay { get; set; }

    /// <summary>The control focus returns to when the sidebar was not opened from a focused element, given the shown content.</summary>
    public Func<object?, InputElement?>? DefaultOpener { get; set; }

    /// <summary>The control that takes focus when the opener is no longer available.</summary>
    public Func<InputElement?>? FallbackFocus { get; set; }

    private void Attach(SidebarViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnSidebarPropertyChanged;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnSidebarPropertyChanged;
        }

        ApplySidebar(viewModel?.IsOpen ?? false);
    }

    private void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarViewModel.IsOpen) && _viewModel is { } viewModel)
        {
            ApplySidebar(viewModel.IsOpen);
        }
    }

    private void ApplySidebar(bool open)
    {
        if (open)
        {
            _opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
            if (_opener is null || ReferenceEquals(_opener, SidebarCloseButton) || Sidebar.IsVisualAncestorOf(_opener))
            {
                _opener = DefaultOpener?.Invoke(_viewModel?.Content);
            }

            // While the sidebar is open the content underneath can be neither clicked nor reached with Tab.
            if (Underlay is { } underlay)
            {
                underlay.IsHitTestVisible = false;
                KeyboardNavigation.SetTabNavigation(underlay, KeyboardNavigationMode.None);
            }

            Sidebar.IsVisible = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_viewModel is not { IsOpen: true })
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
        if (Underlay is { } covered)
        {
            covered.IsHitTestVisible = true;
            KeyboardNavigation.SetTabNavigation(covered, KeyboardNavigationMode.Continue);
        }

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
                if (_viewModel is { IsOpen: true })
                {
                    return;
                }

                var target = opener is { IsEffectivelyVisible: true, Focusable: true } ? opener : FallbackFocus?.Invoke();
                if (target is { IsEffectivelyVisible: true })
                {
                    target.Focus();
                }
            },
            DispatcherPriority.Background);

        // Hide the closed sidebar after the slide-out so it is not exposed to Tab or screen readers.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (_viewModel is not { IsOpen: true })
                {
                    Sidebar.IsVisible = false;
                }
            },
            SlideDuration);
    }
}
