using System.Diagnostics;
using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.App.Shell;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class GameDataViewTests
{
    private sealed class Host
    {
        public required CaptureRig Rig { get; init; }

        public required GameDataViewModel Vm { get; init; }

        public required GameDataView View { get; init; }

        public required Window Window { get; init; }
    }

    private static Host Build(CaptureRig? rig = null)
    {
        rig ??= new CaptureRig();
        var vm = rig.CreateViewModel(action =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        });
        var view = new GameDataView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Flush();
        return new Host { Rig = rig, Vm = vm, View = view, Window = window };
    }

    private static byte[] Named(string cmd) => PlainJson(Envelope(cmd, new Dictionary<string, object?> { ["id"] = 1 }));

    private static FakePacketSource Feeding(IEnumerable<TcpSegment> segments) =>
        new((onSegment, _) =>
        {
            foreach (var segment in segments)
            {
                onSegment(segment);
            }

            return Task.CompletedTask;
        });

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

    private static void ExpandDetails(Host h)
    {
        h.View.Named<Expander>("GameDataCaptureDetails").IsExpanded = true;
        SettleUntil(h.Window, () => h.View.Named<Button>("GameDataRefresh").IsEffectivelyVisible, "the capture details to open");
    }

    private static void Press(Host h, string buttonName) => h.View.Named<Button>(buttonName).Command!.Execute(null);

    private static IEnumerable<string?> TextsNamed(Host h, string name) =>
        h.View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == name).Select(t => t.Text);

    private static Host Captured(params IEnumerable<TcpSegment>[] connections)
    {
        var h = Build(new CaptureRig { Source = Feeding(connections.SelectMany(c => c)) });
        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => h.Vm.HasCharacters && h.View.Named<TextBlock>("GameDataProfileHeadline").IsEffectivelyVisible, "the captured data to appear");
        return h;
    }

    [AvaloniaFact]
    public void The_tab_is_called_Game_data_sits_after_Reputation_and_shows_the_view_model_of_the_container()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };

        window.Show();

        var tabs = window.FindControl<TabControl>("Tabs")!;
        var headers = tabs.Items.OfType<TabItem>().Select(t => t.Header).ToList();
        Assert.Equal(6, headers.Count);
        Assert.Equal(headers.IndexOf("Reputation") + 1, headers.IndexOf("Game data"));
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Game data"));
        SettleUntil(window, () => window.GetVisualDescendants().OfType<GameDataView>().Any(), "the Game data tab to be created");
        Assert.Same(services.GetRequiredService<MainViewModel>().GameData, window.GetVisualDescendants().OfType<GameDataView>().Single().DataContext);
    }

    [AvaloniaFact]
    public void Before_any_capture_the_empty_state_the_privacy_note_and_the_start_button_are_shown()
    {
        var h = Build();
        ExpandDetails(h);

        var empty = h.View.Named<TextBlock>("GameDataEmptyState");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(GameDataViewModel.EmptyText, empty.Text);
        Assert.Equal(GameDataViewModel.PrivacyText, h.View.Named<TextBlock>("GameDataPrivacyText").Text);
        Assert.Equal("Not capturing.", h.View.Named<TextBlock>("GameDataStateText").Text);
        Assert.Equal("Segments seen 0 · dropped 0 · messages read 0 · characters captured 0 · unreadable 0 · profile parts 0 · inventory parts 0", h.View.Named<TextBlock>("GameDataCountersText").Text);
        var start = h.View.Named<Button>("GameDataStartButton");
        Assert.Equal("Start capture", start.Content);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.False(h.View.Named<Button>("GameDataStopButton").IsEffectivelyEnabled);
        Assert.False(h.View.Named<Border>("GameDataProfileCard").IsEffectivelyVisible);
        Assert.False(h.View.Named<Border>("GameDataReputationCard").IsEffectivelyVisible);
        Assert.False(h.View.Named<Border>("GameDataInventoryCard").IsEffectivelyVisible);
        Assert.False(h.View.Named<Button>("GameDataRestartButton").IsEffectivelyVisible);
        Assert.False(h.View.Named<SelectableTextBlock>("GameDataNpcapLink").IsEffectivelyVisible);
        Assert.False(h.View.Named<StackPanel>("GameDataCharacterPanel").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Starting_a_capture_that_reads_a_login_shows_the_profile_a_faction_and_an_item()
    {
        var h = Captured(Connection().Server);

        Assert.Equal("Fictional Hero · Lv 60", h.View.Named<TextBlock>("GameDataProfileHeadline").Text);
        var captured = h.View.Named<TextBlock>("GameDataCapturedText");
        Assert.True(captured.IsEffectivelyVisible);
        Assert.StartsWith("Captured ", captured.Text);
        Assert.EndsWith(" in this session.", captured.Text);
        Assert.False(h.View.Named<TextBlock>("GameDataEmptyState").IsEffectivelyVisible);
        Assert.Contains("Gold: 9,876,543,210", TextsNamed(h, "GameDataProfileLine"));
        SettleUntil(h.Window, () => TextsNamed(h, "GameDataFactionRowName").Contains("Fictional Order"), "the faction rows");
        var names = TextsNamed(h, "GameDataFactionRowName").ToList();
        var ranks = TextsNamed(h, "GameDataFactionRowRank").ToList();
        Assert.Equal("Rank 3", ranks[names.IndexOf("Fictional Order")]);
        Assert.Contains("1,400 / 6,400", TextsNamed(h, "GameDataFactionRowProgress"));
        SettleUntil(h.Window, () => TextsNamed(h, "GameDataItemRowName").Contains("Fictional Blade"), "the item rows");
        var itemNames = TextsNamed(h, "GameDataItemRowName").ToList();
        Assert.Equal("AC · Equipped", TextsNamed(h, "GameDataItemRowFlags").ToList()[itemNames.IndexOf("Fictional Blade")]);
        Assert.Equal("3 items", h.View.Named<TextBlock>("GameDataItemsSummary").Text);
        Assert.Equal("Capture finished. 1 character captured.", h.View.Named<TextBlock>("GameDataStateText").Text);
        Assert.False(h.View.Named<StackPanel>("GameDataCharacterPanel").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Typing_in_the_filter_box_narrows_the_item_list()
    {
        var h = Captured(Connection().Server);
        SettleUntil(h.Window, () => TextsNamed(h, "GameDataItemRowName").Count() == 3, "three item rows");

        h.View.Named<TextBox>("GameDataFilterBox").Text = "blade";

        SettleUntil(h.Window, () => TextsNamed(h, "GameDataItemRowName").SequenceEqual(["Fictional Blade"]), "the filter to narrow the list");
        Assert.Equal("1 of 3 items", h.View.Named<TextBlock>("GameDataItemsSummary").Text);
    }

    [AvaloniaFact]
    public void The_character_selector_is_hidden_with_one_character_and_visible_with_two()
    {
        var one = Captured(Connection().Server);
        Assert.False(one.View.Named<StackPanel>("GameDataCharacterPanel").IsEffectivelyVisible);

        var two = Captured(Connection(clientPort: 50000, name: "First Hero").Server, Connection(clientPort: 50001, name: "Second Hero").Server);
        SettleUntil(two.Window, () => two.Vm.HasSeveralCharacters && two.View.Named<StackPanel>("GameDataCharacterPanel").IsEffectivelyVisible, "the selector");
        var box = two.View.Named<ComboBox>("GameDataCharacterSelector");
        Assert.Equal(2, box.ItemCount);
        var other = two.Vm.SelectedCharacter == "First Hero" ? "Second Hero" : "First Hero";
        Assert.Equal(two.Vm.SelectedCharacter, box.SelectedItem);

        two.Vm.SelectedCharacter = other;

        SettleUntil(two.Window, () => two.View.Named<TextBlock>("GameDataProfileHeadline").Text == other + " · Lv 60", "the other Character's headline");
        Assert.Equal(other, box.SelectedItem);
        Assert.Equal("Character", AutomationProperties.GetName(box));
    }

    [AvaloniaFact]
    public void The_restart_button_is_visible_only_when_the_session_needs_administrator_rights()
    {
        var rig = new CaptureRig();
        rig.Elevation.IsElevated = false;
        var h = Build(rig);
        var restart = h.View.Named<Button>("GameDataRestartButton");
        Assert.False(restart.IsEffectivelyVisible);

        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => restart.IsEffectivelyVisible, "the restart button");

        Assert.Equal("Restart as administrator", restart.Content);
        Assert.Equal("Capturing needs administrator rights.", h.View.Named<TextBlock>("GameDataStateText").Text);
        Assert.True(restart.IsEffectivelyEnabled);

        rig.Elevation.IsElevated = true;
        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => !restart.IsEffectivelyVisible, "the restart button to go away");
    }

    [AvaloniaFact]
    public void The_npcap_address_is_shown_as_selectable_text_when_npcap_is_missing()
    {
        var h = Build(new CaptureRig { Npcap = NpcapStatus.Missing });
        var link = h.View.Named<SelectableTextBlock>("GameDataNpcapLink");
        Assert.False(link.IsEffectivelyVisible);

        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => link.IsEffectivelyVisible, "the Npcap address");

        Assert.Equal("https://npcap.com/", link.Text);
        Assert.Equal(GameDataViewModel.NpcapMissingText, h.View.Named<TextBlock>("GameDataStateText").Text);
        Assert.False(h.View.Named<Button>("GameDataRestartButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_other_commands_line_is_hidden_until_there_are_names_and_then_shows_them()
    {
        var h = Build();
        ExpandDetails(h);
        var line = h.View.Named<SelectableTextBlock>("GameDataOtherNames");
        Assert.False(line.IsEffectivelyVisible);

        var stream = new[] { Named("repGain"), Named("repGain") }.SelectMany(m => m.Append((byte)0)).ToArray();
        h.Rig.Source = Feeding([SynAck(1000), ServerData(1001, stream)]);
        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => line.IsEffectivelyVisible, "the other commands line to appear");

        Assert.Equal("Other commands seen: repGain 2", line.Text);
        Assert.Equal("Other commands seen: repGain 2", AutomationProperties.GetName(line));
        Assert.True(line.Bounds.Bottom <= h.Window.Bounds.Height);
    }

    [AvaloniaFact]
    public void The_hint_line_appears_for_a_stream_without_a_start_and_stays_inside_the_window()
    {
        var h = Build();
        var hint = h.View.Named<TextBlock>("GameDataHint");
        Assert.False(hint.IsEffectivelyVisible);

        h.Rig.Source = Feeding([ServerData(5000, [.. Named("repGain"), 0])]);
        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => hint.IsEffectivelyVisible, "the hint");

        Assert.Equal(GameDataViewModel.EarlierSessionHintText, hint.Text);
        Assert.Equal(GameDataViewModel.EarlierSessionHintText, AutomationProperties.GetName(hint));
        Assert.True(hint.Bounds.Bottom <= h.Window.Bounds.Height);
    }

    [AvaloniaFact]
    public void The_refresh_button_and_the_details_line_exist_with_their_texts()
    {
        var h = Build();
        ExpandDetails(h);

        var refresh = h.View.Named<Button>("GameDataRefresh");
        Assert.Equal("Refresh numbers", refresh.Content);
        Assert.Equal("Refresh the capture numbers", AutomationProperties.GetName(refresh));
        Assert.Same(h.Vm.RefreshCommand, refresh.Command);
        var details = h.View.Named<SelectableTextBlock>("GameDataDetails");
        Assert.False(details.IsEffectivelyVisible);

        var source = new FakePacketSource((_, _) => Task.CompletedTask) { Text = "Devices: 3 found" };
        h.Rig.Source = source;
        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => details.IsEffectivelyVisible, "the details to appear");
        Assert.Equal("Devices: 3 found", details.Text);
    }

    [AvaloniaFact]
    public void The_buttons_and_the_filter_box_have_automation_names()
    {
        var h = Captured(Connection().Server);

        foreach (var name in new[] { "GameDataStartButton", "GameDataStopButton", "GameDataRestartButton" })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(h.View.Named<Button>(name))), name);
        }

        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(h.View.Named<TextBox>("GameDataFilterBox"))));
    }

    [AvaloniaFact]
    public void The_connections_and_samples_lines_are_selectable_and_shown_only_with_content()
    {
        var h = Build();
        ExpandDetails(h);
        var connections = h.View.Named<SelectableTextBlock>("GameDataConnections");
        var samples = h.View.Named<SelectableTextBlock>("GameDataSamples");
        Assert.False(connections.IsEffectivelyVisible);
        Assert.False(samples.IsEffectivelyVisible);

        var garbage = Bytes("Z" + new string('!', 70));
        h.Rig.Source = new FakePacketSource((sink, _) =>
        {
            sink(SynAck(1000));
            sink(ServerData(1001, [.. garbage, 0]));
            return Task.CompletedTask;
        });
        Press(h, "GameDataStartButton");
        SettleUntil(h.Window, () => connections.IsEffectivelyVisible && samples.IsEffectivelyVisible, "the connection lines");

        Assert.StartsWith("Connections: 1 seen", connections.Text);
        Assert.StartsWith("Unreadable messages: #1", samples.Text);
        Assert.Equal(samples.Text, AutomationProperties.GetName(samples));
    }

    private static byte[] GoldExp(int gold, int exp) =>
        PlainJson(Envelope("addGoldExp", new Dictionary<string, object?> { ["intGold"] = gold, ["intExp"] = exp, ["typ"] = "m", ["id"] = 4 }));

    [AvaloniaFact]
    public void The_capture_details_are_in_a_collapsed_expander_that_holds_the_refresh_button()
    {
        var h = Build();

        var expander = h.View.Named<Expander>("GameDataCaptureDetails");
        Assert.False(expander.IsExpanded);
        Assert.Equal("Capture details", expander.Header);
        var content = Assert.IsAssignableFrom<Panel>(expander.Content);
        var refresh = content.Children.OfType<Button>().Single(b => b.Name == "GameDataRefresh");
        Assert.Same(h.Vm.RefreshCommand, refresh.Command);
        Assert.Contains(content.Children, c => c.Name == "GameDataCountersText");
        Assert.Contains(content.GetLogicalDescendants().OfType<SelectableTextBlock>(), t => t.Name == "GameDataCommands");

        ExpandDetails(h);

        Assert.True(h.View.Named<Button>("GameDataRefresh").IsEffectivelyVisible);
        Assert.Equal("Refresh numbers", h.View.Named<Button>("GameDataRefresh").Content);
    }

    [AvaloniaFact]
    public void Nothing_but_the_state_the_hint_and_the_privacy_text_stays_outside_the_details()
    {
        var h = Build();

        var card = h.View.Named<Border>("GameDataCaptureCard");
        var outside = card.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => c.Name is { Length: > 0 } && c.Name.StartsWith("GameData", StringComparison.Ordinal))
            .Where(c => !c.GetLogicalAncestors().OfType<Expander>().Any() && c is not Expander)
            .Select(c => c.Name)
            .ToList();

        Assert.Equal(
            ["GameDataTitle", "GameDataStartButton", "GameDataStopButton", "GameDataRestartButton", "GameDataStateText", "GameDataHint", "GameDataNpcapLink", "GameDataPrivacyText"],
            outside);
    }

    [AvaloniaFact]
    public void Live_updates_show_in_the_profile_without_pressing_anything()
    {
        var traffic = Connection();
        traffic.Server.Add(ServerData(traffic.NextServerSequence, [.. GoldExp(40, 15), 0, .. GoldExp(1200, 340), 0]));
        var h = Captured(traffic.Server);

        SettleUntil(h.Window, () => TextsNamed(h, "GameDataProfileLine").Contains("Gold: 9,876,544,450"), "the gold to follow the live events");
        var live = h.View.Named<TextBlock>("GameDataLiveText");
        var gain = h.View.Named<TextBlock>("GameDataLastGain");
        SettleUntil(h.Window, () => live.IsEffectivelyVisible && gain.IsEffectivelyVisible, "the live lines");

        Assert.StartsWith("Live: 2 updates since ", live.Text);
        Assert.EndsWith("Buying, quests, reputation and level-ups are not tracked yet; log in again to refresh.", live.Text);
        Assert.Equal("Last kill: +1,200 gold, +340 exp", gain.Text);
        Assert.Equal(gain.Text, AutomationProperties.GetName(gain));
        Assert.Equal(live.Text, AutomationProperties.GetName(live));
    }

    [AvaloniaFact]
    public void The_live_lines_are_hidden_without_updates()
    {
        var h = Captured(Connection().Server);

        Assert.False(h.View.Named<TextBlock>("GameDataLiveText").IsEffectivelyVisible);
        Assert.False(h.View.Named<TextBlock>("GameDataLastGain").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_privacy_text_is_the_planned_one_and_stays_visible_with_the_details_collapsed()
    {
        var h = Build();

        var privacy = h.View.Named<TextBlock>("GameDataPrivacyText");

        Assert.True(privacy.IsEffectivelyVisible);
        Assert.Equal(
            "It starts by itself when the app opens and listens until you press Stop capture. It reads your login data and your own gold, experience, item and class point events, and never your e-mail address, IP address, password or other players' data.",
            privacy.Text);
    }
}
