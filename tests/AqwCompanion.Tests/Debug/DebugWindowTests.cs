using AqwCompanion.App.Debug;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AqwCompanion.Tests.Debug;

public class DebugWindowTests
{
    [AvaloniaFact]
    public void The_debug_window_holds_the_capture_card_and_the_diagnostics_export()
    {
        var built = MainViewModelBuilder.Create();
        var window = new DebugWindow { DataContext = new DebugViewModel(built.Main.GameData, built.Main.Diagnostics, built.Main.HardFarms) };

        window.Show();

        var capture = window.FindControl<CaptureDebugView>("DebugCapture")!;
        Assert.Same(built.Main.GameData, capture.DataContext);
        Assert.NotNull(capture.FindControl<Button>("GameDataStartButton"));
        var diagnostics = window.FindControl<DiagnosticsView>("DebugDiagnostics")!;
        Assert.Same(built.Main.Diagnostics, diagnostics.DataContext);
    }

    [AvaloniaFact]
    public void The_hard_farms_report_is_in_a_collapsed_section_bound_to_the_hard_farms_view_model()
    {
        var built = MainViewModelBuilder.Create();
        var window = new DebugWindow { DataContext = new DebugViewModel(built.Main.GameData, built.Main.Diagnostics, built.Main.HardFarms) };

        window.Show();

        var section = window.FindControl<Expander>("DebugHardFarmsReport")!;
        Assert.False(section.IsExpanded);
        Assert.Same(built.Main.HardFarms, section.DataContext);
        section.IsExpanded = true;
        var text = window.FindControl<SelectableTextBlock>("DebugHardFarmsReportText")!;
        Assert.Equal(built.Main.HardFarms.MissingReportText, text.Text);
    }
}
