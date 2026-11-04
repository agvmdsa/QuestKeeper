namespace AqwCompanion.App.ViewModels;

public sealed class MainViewModel(WikiViewModel wiki, DiagnosticsViewModel diagnostics)
{
    public string Title { get; } = AppInfo.ProductName;
    public WikiViewModel Wiki { get; } = wiki;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
}
