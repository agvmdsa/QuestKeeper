using AqwCompanion.App.Phone;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace AqwCompanion.Tests.Phone;

public class PairingWindowTests
{
    private sealed class Links(PhoneRegistry registry) : IPairingLinkSource
    {
        public string UnavailableText => "Tailscale is not installed.";

        public string? CreatePairingLink() => "https://pc.tail1.ts.net/#t=" + registry.CreatePairingToken();
    }

    private static (PairingWindow Window, PairingViewModel Vm, PhoneRegistry Registry) Open()
    {
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), () => DateTimeOffset.UtcNow);
        var vm = new PairingViewModel(new Links(registry), registry, () => DateTimeOffset.UtcNow);
        var window = new PairingWindow { DataContext = vm };
        window.Show();
        return (window, vm, registry);
    }

    [AvaloniaFact]
    public void It_shows_the_qr_image_the_link_the_buttons_and_the_empty_state()
    {
        var (window, vm, _) = Open();

        Assert.NotNull(window.Named<Image>("PairingQrImage").Source);
        var link = window.Named<SelectableTextBlock>("PairingLinkText");
        Assert.Equal(vm.Link, link.Text);
        Assert.False(window.Named<Button>("PairingCopyButton").IsEffectivelyEnabled);
        Assert.NotNull(window.Named<Button>("PairingNewLinkButton"));
        Assert.True(window.Named<TextBlock>("PairingNoDevices").IsEffectivelyVisible);
        Assert.Equal("No phones paired yet.", window.Named<TextBlock>("PairingNoDevices").Text);
        Assert.Equal(PairingViewModel.ExplainText, window.Named<TextBlock>("PairingExplain").Text);
        Assert.False(window.Named<Button>("PairingRevokeAllButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void A_paired_device_shows_a_row_with_a_revoke_button()
    {
        var (window, vm, registry) = Open();

        registry.TryPair(vm.Link[(vm.Link.IndexOf("#t=", StringComparison.Ordinal) + 3)..], "My phone");
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(window.Named<Button>("PairingRevokeAllButton").IsEffectivelyVisible);
        var buttons = window.Named<ItemsControl>("PairingDevices").GetVisualDescendants().OfType<Button>().Where(b => b.Content as string == "Revoke").ToList();
        Assert.Single(buttons);
        buttons[0].Command!.Execute(null);
        Assert.Empty(registry.List());
        Assert.True(window.Named<TextBlock>("PairingNoDevices").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_token_appears_only_in_the_link_text()
    {
        var (window, vm, _) = Open();
        var token = vm.Link[(vm.Link.IndexOf("#t=", StringComparison.Ordinal) + 3)..];

        Assert.DoesNotContain(token, window.Title);
        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (control.Name == "PairingLinkText")
            {
                continue;
            }

            Assert.DoesNotContain(token, (control as TextBlock)?.Text ?? string.Empty);
            Assert.DoesNotContain(token, (control as Button)?.Content as string ?? string.Empty);
            Assert.DoesNotContain(token, Avalonia.Automation.AutomationProperties.GetName(control) ?? string.Empty);
        }
    }

    [AvaloniaFact]
    public void Closing_the_window_disposes_the_view_model()
    {
        var (window, vm, registry) = Open();

        window.Close();
        registry.TryPair(registry.CreatePairingToken(), "Late");

        Assert.Empty(vm.Devices);
    }

    [AvaloniaFact]
    public void An_expired_link_replaces_the_code_with_the_notice()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), clock.GetUtcNow);
        var vm = new PairingViewModel(new Links(registry), registry, clock.GetUtcNow);
        var window = new PairingWindow { DataContext = vm };
        window.Show();

        clock.Advance(TimeSpan.FromMinutes(6));
        vm.Tick();

        Assert.Null(window.Named<Image>("PairingQrImage").Source);
        Assert.Equal("This link expired. Create a new one.", window.Named<TextBlock>("PairingMessage").Text);
        Assert.False(window.Named<SelectableTextBlock>("PairingLinkText").IsEffectivelyVisible);
    }
}
