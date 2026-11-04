using AqwCompanion.Core.Ingestion;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.ViewModels;

public partial class WikiViewModel : ObservableObject
{
    private readonly IBrowserHost _host;

    [ObservableProperty]
    private string _addressInput = "https://aqwwiki.wikidot.com/";

    [ObservableProperty]
    private string _currentUrl = string.Empty;

    public WikiViewModel(IBrowserHost host)
    {
        _host = host;
        _host.NavigationCompleted += (_, e) => CurrentUrl = e.Url;
    }

    [RelayCommand]
    private void Go() => _host.Navigate(AddressInput);
}
