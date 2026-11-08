using AqwCompanion.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Diagnostics;

public partial class DiagnosticsViewModel(AppPaths paths, DiagnosticsExporter exporter) : ObservableObject
{
    [ObservableProperty]
    private string _status = string.Empty;

    public string LogDirectory => paths.LogDirectory;

    [RelayCommand]
    private void Export()
    {
        try
        {
            var output = Path.Combine(paths.ExportDirectory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            exporter.Export(new DiagnosticsRequest(
                paths.LogDirectory, AppInfo.Version, output));
            Status = $"Exported to {output}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Diagnostics export failed");
            Status = $"Export failed ({ex.GetType().Name}).";
        }
    }
}
