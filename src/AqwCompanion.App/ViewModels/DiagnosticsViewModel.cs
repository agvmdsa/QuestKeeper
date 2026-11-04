using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Ingestion;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.ViewModels;

public partial class DiagnosticsViewModel(
    AppPaths paths,
    DiagnosticsExporter exporter,
    FailedSnapshotBuffer failures) : ObservableObject
{
    [ObservableProperty]
    private bool _includeSnapshot;

    [ObservableProperty]
    private string _status = string.Empty;

    public string LogDirectory => paths.LogDirectory;

    [RelayCommand]
    private void Export()
    {
        var output = Path.Combine(paths.ExportDirectory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        try
        {
            exporter.Export(new DiagnosticsRequest(
                paths.LogDirectory, AppInfo.Version, output, failures.Latest, IncludeSnapshot));
            Status = $"Exported to {output}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }
}
