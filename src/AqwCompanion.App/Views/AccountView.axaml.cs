using System.ComponentModel;
using AqwCompanion.App.Browser;
using AqwCompanion.App.ViewModels;
using Avalonia.Controls;

namespace AqwCompanion.App.Views;

/// <summary>Hosts the embedded browser. The native control is only created once the user asks to sign in.</summary>
public partial class AccountView : UserControl
{
    private AccountViewModel? _viewModel;

    public AccountView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind(DataContext as AccountViewModel);
    }

    private void Bind(AccountViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            EnsureBrowser();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.IsBrowserOpen))
        {
            EnsureBrowser();
        }
    }

    private void EnsureBrowser()
    {
        if (_viewModel is not { IsBrowserOpen: true, Browser: WebViewBrowserHost { IsAttached: false } host })
        {
            return;
        }

        var webView = new NativeWebView();
        host.Attach(webView);
        BrowserSlot.Child = webView;
    }
}
