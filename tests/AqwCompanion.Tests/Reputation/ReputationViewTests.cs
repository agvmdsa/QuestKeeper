using System.Diagnostics;
using System.Text;
using AqwCompanion.App;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Reputation;

public class ReputationViewTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private sealed class Host
    {
        public required ReputationViewModel Vm { get; init; }

        public required ReputationView View { get; init; }

        public required Window Window { get; init; }

        public required FakeReputationFilePicker Picker { get; init; }

        public required FakeReputationAutoSource Auto { get; init; }
    }

    private static Host Build()
    {
        var picker = new FakeReputationFilePicker();
        var auto = new FakeReputationAutoSource();
        var vm = new ReputationViewModel(new ReputationStore(new InMemoryStoredDataStore()), picker, auto, new FixedClock());
        var view = new ReputationView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Flush();
        SettleUntil(window, () => vm.CheckedLine.Length > 0, "the first refresh on opening to finish");
        return new Host { Vm = vm, View = view, Window = window, Picker = picker, Auto = auto };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    // Pumps layout, render ticks and queued jobs until the condition holds; never a fixed number of passes.
    private static void SettleUntil(Window window, Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Flush();
            if (condition())
            {
                return;
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Timed out waiting for: {what}.");
            Thread.Sleep(5);
        }
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static IEnumerable<string?> TextsNamed(Host h, string name) =>
        h.View.Named<ListBox>("ReputationList").GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == name).Select(t => t.Text);

    [AvaloniaFact]
    public void The_tab_is_called_Reputation_and_sits_after_Hard_Farms()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        var tabs = window.FindControl<TabControl>("Tabs")!;
        var headers = tabs.Items.OfType<TabItem>().Select(t => t.Header).ToList();
        Assert.Equal(headers.IndexOf("Hard Farms") + 1, headers.IndexOf("Reputation"));
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Reputation"));
        SettleUntil(window, () => window.GetVisualDescendants().OfType<ReputationView>().Any(), "the Reputation tab to be created");
        Assert.Same(services.GetRequiredService<MainViewModel>().Reputation, window.GetVisualDescendants().OfType<ReputationView>().Single().DataContext);
    }

    [AvaloniaFact]
    public void Before_any_import_the_empty_state_and_the_import_button_are_shown()
    {
        var h = Build();

        var empty = h.View.Named<TextBlock>("ReputationEmptyState");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(ReputationViewModel.EmptyText, empty.Text);
        var button = h.View.Named<Button>("ReputationImportButton");
        Assert.Equal("Import reputation file…", button.Content);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.False(h.View.Named<Border>("ReputationCard").IsEffectivelyVisible);
        Assert.False(h.View.Named<TextBlock>("ReputationImportedLine").IsEffectivelyVisible);
        Assert.False(h.View.Named<TextBlock>("ReputationErrorLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_tab_reads_the_game_client_file_when_it_becomes_visible_and_shows_the_data()
    {
        var picker = new FakeReputationFilePicker();
        var auto = new FakeReputationAutoSource();
        auto.Returns("""{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":2,"rep":5,"repToRank":9}]}""");
        var vm = new ReputationViewModel(new ReputationStore(new InMemoryStoredDataStore()), picker, auto, new FixedClock());
        var view = new ReputationView { DataContext = vm, IsVisible = false };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Flush();
        Assert.Equal(0, auto.OpenCalls);

        view.IsVisible = true;
        SettleUntil(window, () => view.GetVisualDescendants().OfType<ListBox>().Any()
            && view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "ReputationRowName" && t.Text == "Good"), "the standings to appear");

        Assert.Equal("1 factions loaded from the game client.", view.Named<TextBlock>("ReputationStatusLine").Text);
        Assert.Equal(1, auto.OpenCalls);
        Assert.True(view.Named<TextBlock>("ReputationCheckedLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_refresh_button_has_an_accessible_name_and_the_expected_path_can_be_selected()
    {
        var h = Build();

        var refresh = h.View.Named<Button>("ReputationRefreshButton");
        Assert.Equal(ReputationViewModel.RefreshAccessibleName, AutomationProperties.GetName(refresh));
        var path = h.View.Named<SelectableTextBlock>("ReputationPathLine");
        Assert.Contains(h.Auto.Path, path.Text);
    }

    [AvaloniaFact]
    public void The_interactive_controls_have_accessible_names()
    {
        var h = Build();

        foreach (var name in new[] { "ReputationImportButton", "ReputationList" })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(h.View.Named<Control>(name))), name);
        }
    }

    [AvaloniaFact]
    public async Task After_an_import_the_list_is_sorted_by_faction_and_the_imported_line_names_the_file()
    {
        var h = Build();
        h.Picker.Returns(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reputation", "Samples", "reputation-sample.json")), "reputation-sample.json");

        await h.Vm.ImportCommand.ExecuteAsync(null);
        SettleUntil(h.Window, () => TextsNamed(h, "ReputationRowName").Count() == 4, "4 rows to be realized");

        Assert.False(h.View.Named<TextBlock>("ReputationEmptyState").IsEffectivelyVisible);
        Assert.Equal(["alchemy", "Evil", "Example Guild", "Good"], TextsNamed(h, "ReputationRowName"));
        Assert.Equal(["10", "3", "1", "5"], TextsNamed(h, "ReputationRowRank"));
        Assert.Equal(["0", "410", "80", "1,234"], TextsNamed(h, "ReputationRowRep"));
        Assert.Equal(["0", "975", "200", "5,000"], TextsNamed(h, "ReputationRowToNext"));
        var line = h.View.Named<TextBlock>("ReputationImportedLine");
        Assert.True(line.IsEffectivelyVisible);
        Assert.StartsWith("Imported ", line.Text);
        Assert.EndsWith(" from reputation-sample.json", line.Text);
        Assert.Equal("4 factions imported.", h.View.Named<TextBlock>("ReputationStatusLine").Text);
    }

    [AvaloniaFact]
    public async Task The_column_titles_are_shown_with_the_list()
    {
        var h = Build();
        h.Picker.Returns("""{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":1,"rep":1,"repToRank":1}]}""");

        await h.Vm.ImportCommand.ExecuteAsync(null);
        SettleUntil(h.Window, () => TextsNamed(h, "ReputationRowName").Any(), "a row to be realized");

        var titles = h.View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("columnTitle")).Select(t => t.Text);
        Assert.Equal(["Faction", "Rank", "Rep", "To next rank"], titles);
    }

    [AvaloniaFact]
    public async Task An_invalid_file_shows_the_error_and_keeps_the_list()
    {
        var h = Build();
        h.Picker.Returns("""{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":1,"rep":1,"repToRank":1}]}""");
        await h.Vm.ImportCommand.ExecuteAsync(null);
        h.Picker.OnPick = () => Task.FromResult<PickedFile?>(new PickedFile(new MemoryStream(Encoding.UTF8.GetBytes("junk")), "bad.json"));

        await h.Vm.ImportCommand.ExecuteAsync(null);
        SettleUntil(h.Window, () => h.View.Named<TextBlock>("ReputationErrorLine").IsEffectivelyVisible, "the error to be shown");

        Assert.Contains("not valid JSON", h.View.Named<TextBlock>("ReputationErrorLine").Text);
        Assert.Equal(["Good"], TextsNamed(h, "ReputationRowName"));
    }

    [AvaloniaFact]
    public async Task Reset_returns_the_view_to_the_empty_state()
    {
        var h = Build();
        h.Picker.Returns("""{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":1,"rep":1,"repToRank":1}]}""");
        await h.Vm.ImportCommand.ExecuteAsync(null);
        SettleUntil(h.Window, () => !h.View.Named<TextBlock>("ReputationEmptyState").IsEffectivelyVisible, "the list to be shown");

        h.Vm.Reset();
        SettleUntil(h.Window, () => h.View.Named<TextBlock>("ReputationEmptyState").IsEffectivelyVisible, "the empty state to return");

        Assert.False(h.View.Named<Border>("ReputationCard").IsEffectivelyVisible);
    }
}
