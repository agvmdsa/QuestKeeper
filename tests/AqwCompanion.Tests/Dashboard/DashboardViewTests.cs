using System.Diagnostics;
using AqwCompanion.App.Dashboard;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Shell;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Dashboard;

public class DashboardViewTests
{
    private sealed record Host(MainViewModelBuilder.Built Built, MainWindow Window)
    {
        public MainViewModel Main => Built.Main;

        public T Named<T>(string name) where T : Control => Window.Named<T>(name);

        public IEnumerable<string?> Texts(string name) =>
            Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == name).Select(t => t.Text);
    }

    private static Host Build(IEnumerable<IEnumerable<TcpSegment>>? connections = null, int height = 720)
    {
        var rig = new CaptureRig();
        if (connections is not null)
        {
            var all = connections.SelectMany(c => c).ToList();
            rig.Source = new FakePacketSource((onSegment, _) =>
            {
                foreach (var segment in all)
                {
                    onSegment(segment);
                }

                return Task.CompletedTask;
            });
        }

        var built = MainViewModelBuilder.Create(rig, action =>
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
        var window = new MainWindow { DataContext = built.Main, Width = 1100, Height = height };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return new Host(built, window);
    }

    private static Host Captured(params IEnumerable<TcpSegment>[] connections)
    {
        var h = Build(connections);
        h.Main.GameData.StartCommand.Execute(null);
        SettleUntil(h.Window, () => h.Main.GameData.HasCharacters, "the captured Character");
        return h;
    }

    private static void SettleUntil(Window window, Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Timed out waiting for: {what}.");
            Thread.Sleep(5);
        }
    }

    private static byte[] GoldExp(int gold, int exp) =>
        PlainJson(Envelope("addGoldExp", new Dictionary<string, object?> { ["intGold"] = gold, ["intExp"] = exp, ["typ"] = "m", ["id"] = 4 }));

    // The top edge of a control in the window's coordinates, summed up the visual parents (nothing here is transformed).
    private static double TopOf(Control control, Avalonia.Visual root)
    {
        double top = 0;
        for (Avalonia.Visual? v = control; v is not null && !ReferenceEquals(v, root); v = v.GetVisualParent())
        {
            top += v.Bounds.Y;
        }

        return top;
    }

    [AvaloniaFact]
    public void The_window_has_no_tabs_and_shows_the_dashboard()
    {
        var h = Build();

        Assert.Empty(h.Window.GetVisualDescendants().OfType<TabControl>());
        Assert.Single(h.Window.GetVisualDescendants().OfType<DashboardView>());
    }

    [AvaloniaFact]
    public void Before_any_login_the_dashboard_says_it_is_waiting_and_hides_the_character_strip()
    {
        var h = Build();

        var empty = h.Named<TextBlock>("DashboardEmptyState");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(GameDataViewModel.EmptyText, empty.Text);
        Assert.False(h.Named<Border>("DashboardProfileCard").IsEffectivelyVisible);
        Assert.True(h.Named<TextBlock>("DashboardFactionsEmpty").IsEffectivelyVisible);
        Assert.Equal(GameDataViewModel.IdleText, h.Named<TextBlock>("FooterText").Text);
    }

    [AvaloniaFact]
    public void A_captured_login_fills_the_character_strip_and_orders_the_factions_by_what_is_missing()
    {
        var h = Captured(Connection().Server);

        Assert.False(h.Named<Border>("DashboardEmptyCard").IsEffectivelyVisible);
        Assert.Equal("Fictional Hero · Lv 60", h.Named<TextBlock>("DashboardProfileHeadline").Text);
        var figures = h.Named<ItemsControl>("DashboardFigures");
        SettleUntil(h.Window, () => figures.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "9,876,543,210"), "the figures");
        var texts = figures.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Gold", texts);
        Assert.Contains("123,456 / 654,321", texts);
        Assert.StartsWith("Captured ", h.Named<TextBlock>("DashboardCapturedText").Text);

        SettleUntil(h.Window, () => h.Texts("DashboardFactionName").Count() == 3, "the faction rows");
        Assert.Equal(["Fresh Order", "Fictional Order", "Other Order"], h.Texts("DashboardFactionName"));
        Assert.Contains("900 to next", h.Texts("DashboardFactionToNext"));
    }

    [AvaloniaFact]
    public void Live_gold_and_the_last_kill_show_without_pressing_anything()
    {
        var traffic = Connection();
        traffic.Server.Add(ServerData(traffic.NextServerSequence, [.. GoldExp(40, 15), 0, .. GoldExp(1200, 340), 0]));
        var h = Captured(traffic.Server);

        var gain = h.Named<TextBlock>("DashboardLastGain");
        SettleUntil(h.Window, () => gain.IsEffectivelyVisible, "the last kill line");

        Assert.Equal("Last kill: +1,200 gold, +340 exp", gain.Text);
        Assert.Equal(gain.Text, AutomationProperties.GetName(gain));
        SettleUntil(
            h.Window,
            () => h.Named<ItemsControl>("DashboardFigures").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "9,876,544,450"),
            "the gold to follow the live events");
    }

    [AvaloniaFact]
    public void The_last_kill_line_is_hidden_without_live_updates()
    {
        var h = Captured(Connection().Server);

        Assert.False(h.Named<TextBlock>("DashboardLastGain").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void One_character_is_named_in_the_top_bar_and_several_get_a_selector()
    {
        var one = Captured(Connection().Server);
        Assert.True(one.Named<TextBlock>("CharacterSummaryText").IsEffectivelyVisible);
        Assert.Equal("Fictional Hero", one.Named<TextBlock>("CharacterSummaryText").Text);
        Assert.False(one.Named<ComboBox>("CharacterSelector").IsEffectivelyVisible);

        var two = Captured(Connection(clientPort: 50000, name: "First Hero").Server, Connection(clientPort: 50001, name: "Second Hero").Server);
        var box = two.Named<ComboBox>("CharacterSelector");
        SettleUntil(two.Window, () => box.IsEffectivelyVisible, "the selector");
        Assert.False(two.Named<TextBlock>("CharacterSummaryText").IsEffectivelyVisible);
        Assert.Equal(2, box.ItemCount);
        Assert.Equal("Character", AutomationProperties.GetName(box));

        var other = two.Main.GameData.SelectedCharacter == "First Hero" ? "Second Hero" : "First Hero";
        two.Main.GameData.SelectedCharacter = other;

        SettleUntil(two.Window, () => two.Named<TextBlock>("DashboardProfileHeadline").Text == other + " · Lv 60", "the other Character's headline");
        Assert.Equal(other, box.SelectedItem);
    }

    [AvaloniaFact]
    public void The_footer_follows_the_capture_and_says_whether_the_data_is_live_or_saved()
    {
        var h = Captured(Connection().Server);

        Assert.Equal(h.Main.GameData.FooterText, h.Named<TextBlock>("FooterText").Text);
        var captured = h.Named<TextBlock>("FooterCaptured");
        Assert.True(captured.IsEffectivelyVisible);
        Assert.StartsWith("Captured ", captured.Text);
    }

    [AvaloniaFact]
    public void The_settings_button_opens_the_settings_dialog()
    {
        var h = Build();
        var button = h.Named<Button>("SettingsButton");

        button.Command!.Execute(null);

        Assert.Same(h.Main.Settings, Assert.Single(h.Built.Dialog.Shown));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
    }

    [AvaloniaFact]
    public void The_hard_farms_card_shows_the_hard_farms_view_model()
    {
        var h = Build();

        var view = h.Named<HardFarmsView>("DashboardHardFarms");

        Assert.Same(h.Main.HardFarms, view.DataContext);
        Assert.True(h.Named<Border>("DashboardHardFarmsCard").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_cards_stay_inside_the_window_at_the_smallest_height()
    {
        var h = Captured(Connection().Server);
        h.Window.Height = 600;
        SettleUntil(h.Window, () => h.Window.Bounds.Height <= 600.5, "the window to shrink");
        h.Window.UpdateLayout();

        var footerTop = TopOf(h.Named<Border>("Footer"), h.Window);
        foreach (var name in new[] { "DashboardProfileCard", "DashboardReputationCard", "DashboardHardFarmsCard" })
        {
            var card = h.Named<Border>(name);
            var bottom = TopOf(card, h.Window) + card.Bounds.Height;
            Assert.True(bottom <= footerTop + 0.5, $"{name} reaches {bottom} but the footer starts at {footerTop}");
        }
    }
}
