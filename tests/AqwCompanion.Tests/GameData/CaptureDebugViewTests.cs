using System.Diagnostics;
using AqwCompanion.App.GameData;
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
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.GameData;

public class CaptureDebugViewTests
{
    private sealed class Host
    {
        public required CaptureRig Rig { get; init; }

        public required GameDataViewModel Vm { get; init; }

        public required CaptureDebugView View { get; init; }

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
        var view = new CaptureDebugView { DataContext = vm };
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
        SettleUntil(h.Window, () => h.Vm.HasCharacters, "the captured data to appear");
        return h;
    }

    [AvaloniaFact]
    public void Before_any_capture_the_privacy_note_the_state_and_the_start_button_are_shown()
    {
        var h = Build();
        ExpandDetails(h);

        Assert.Equal(GameDataViewModel.PrivacyText, h.View.Named<TextBlock>("GameDataPrivacyText").Text);
        Assert.Equal("Not capturing.", h.View.Named<TextBlock>("GameDataStateText").Text);
        Assert.Equal("Segments seen 0 · dropped 0 · messages read 0 · characters captured 0 · unreadable 0 · profile parts 0 · inventory parts 0", h.View.Named<TextBlock>("GameDataCountersText").Text);
        var start = h.View.Named<Button>("GameDataStartButton");
        Assert.Equal("Start capture", start.Content);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.False(h.View.Named<Button>("GameDataStopButton").IsEffectivelyEnabled);
        Assert.False(h.View.Named<Button>("GameDataRestartButton").IsEffectivelyVisible);
        Assert.False(h.View.Named<SelectableTextBlock>("GameDataNpcapLink").IsEffectivelyVisible);
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
    public void The_buttons_have_automation_names()
    {
        var h = Captured(Connection().Server);

        foreach (var name in new[] { "GameDataStartButton", "GameDataStopButton", "GameDataRestartButton" })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(h.View.Named<Button>(name))), name);
        }
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

    [AvaloniaFact]
    public void The_capture_details_are_in_an_expander_that_is_open_in_the_debug_window()
    {
        var h = Build();

        var expander = h.View.Named<Expander>("GameDataCaptureDetails");
        Assert.True(expander.IsExpanded);
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
    public void The_privacy_text_is_the_planned_one_and_stays_visible()
    {
        var h = Build();

        var privacy = h.View.Named<TextBlock>("GameDataPrivacyText");

        Assert.True(privacy.IsEffectivelyVisible);
        Assert.Equal(
            "It starts by itself when the app opens and listens until you press Stop capture. It reads your login data and your own gold, experience, item and class point events, and never your e-mail address, IP address, password or other players' data.",
            privacy.Text);
    }
}
