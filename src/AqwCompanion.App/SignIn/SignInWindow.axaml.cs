using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AqwCompanion.App.SignIn;

/// <summary>
/// The modal sign-in window. It is a plain view: whoever shows it creates the browser control and hands it
/// over with <see cref="SetBrowser"/>, so building the window never touches the native browser.
/// </summary>
public partial class SignInWindow : Window
{
    private static readonly Size FullSize = new(460, 640);
    private static readonly Size CompactSize = new(360, 140);

    private SignInViewModel? _viewModel;

    public SignInWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind(DataContext as SignInViewModel);
        Opened += (_, _) => Dispatcher.UIThread.Post(FocusDefault);
        Closing += OnClosing;
        Closed += (_, _) => Bind(null);
    }

    public SignInWindow(SignInViewModel viewModel)
        : this() => DataContext = viewModel;

    private void Bind(SignInViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplySize();
        }
    }

    /// <summary>Puts the browser control in (or, with null, takes it out of) the layout.</summary>
    public void SetBrowser(Control? browser) => BrowserSlot.Child = browser;

    private void OnCloseRequested(object? sender, SignInResult result) => Close();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel is not { IsFinished: false }
            || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        // Closed some other way (Alt+F4, the window manager): route it through the view model so the
        // in-flight read is cancelled and the caller gets a result.
        e.Cancel = true;
        Dispatcher.UIThread.Post(_viewModel.Cancel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SignInViewModel.Mode):
                ApplySize();
                CenterOnOwner();
                Dispatcher.UIThread.Post(FocusDefault);
                break;
            case nameof(SignInViewModel.State):
                Dispatcher.UIThread.Post(FocusDefault);
                break;
        }
    }

    private void ApplySize()
    {
        var size = _viewModel is { IsCompact: true } ? CompactSize : FullSize;
        Width = size.Width;
        Height = size.Height;
    }

    private void CenterOnOwner()
    {
        if (!IsVisible || Owner is not Window owner)
        {
            return;
        }

        var ownerWidth = owner.Bounds.Width * owner.RenderScaling;
        var ownerHeight = owner.Bounds.Height * owner.RenderScaling;
        var x = owner.Position.X + (int)((ownerWidth - (Width * RenderScaling)) / 2);
        var y = owner.Position.Y + (int)((ownerHeight - (Height * RenderScaling)) / 2);
        Position = new PixelPoint(x, y);
    }

    /// <summary>Initial focus goes to a control inside the popup, never to the (native) page.</summary>
    private void FocusDefault()
    {
        if (_viewModel is not { IsFinished: false })
        {
            return;
        }

        Control target = _viewModel switch
        {
            { CanTryAgain: true } => TryAgainButton,
            { IsError: true } => ErrorCloseButton,
            { IsCompact: true } => CancelButton,
            _ => CloseButton,
        };
        target.Focus();
    }
}
