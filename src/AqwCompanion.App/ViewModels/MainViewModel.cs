namespace AqwCompanion.App.ViewModels;

public sealed class MainViewModel(AccountViewModel account, WikiViewModel wiki, DiagnosticsViewModel diagnostics)
{
    public string Title { get; } = AppInfo.ProductName;
    public AccountViewModel Account { get; } = account;
    public WikiViewModel Wiki { get; } = wiki;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
}
