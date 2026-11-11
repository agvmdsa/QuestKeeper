using AqwCompanion.App.Phone;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PairingViewModelTests
{
    private sealed class FakeLinks(PhoneRegistry registry) : IPairingLinkSource
    {
        public bool Available { get; set; } = true;

        public bool IsAvailable => Available;

        public string UnavailableText => "Tailscale is not installed.";

        public PairingOffer? CreateOffer()
        {
            if (!Available)
            {
                return null;
            }

            var token = registry.CreatePairingToken();
            return new PairingOffer("https://pc.tail1.ts.net/#t=" + token, token);
        }
    }

    private static (PairingViewModel Vm, PhoneRegistry Registry, FakeLinks Links, ManualClock Clock) Build(bool available = true, Action<Action>? post = null)
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), clock.GetUtcNow);
        var links = new FakeLinks(registry) { Available = available };
        return (new PairingViewModel(links, registry, clock.GetUtcNow, post), registry, links, clock);
    }

    private static string Token(string link) => link[(link.IndexOf("#t=", StringComparison.Ordinal) + 3)..];

    [Fact]
    public void Opening_creates_a_link_and_a_qr_code()
    {
        var (vm, _, _, _) = Build();

        Assert.StartsWith("https://pc.tail1.ts.net/#t=", vm.Link);
        Assert.True(vm.HasLink);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], vm.QrPng.Take(4));
        Assert.Equal(string.Empty, vm.Message);
        Assert.Equal("Expires in 5:00", vm.ExpiresText);
    }

    [Fact]
    public void Regenerate_makes_a_new_link_whose_token_pairs_once()
    {
        var (vm, registry, _, _) = Build();
        var first = vm.Link;

        vm.Regenerate();

        Assert.NotEqual(first, vm.Link);
        Assert.Equal(PairStatus.Paired, registry.TryPair(Token(vm.Link), "Phone").Status);
        Assert.Equal(PairStatus.Invalid, registry.TryPair(Token(vm.Link), "Phone").Status);
    }

    [Fact]
    public void Regenerate_cancels_the_old_token_so_it_no_longer_pairs()
    {
        var (vm, registry, _, _) = Build();
        var old = Token(vm.Link);

        vm.Regenerate();

        Assert.Equal(PairStatus.Invalid, registry.TryPair(old, "Phone").Status);
        Assert.Equal(PairStatus.Paired, registry.TryPair(Token(vm.Link), "Phone").Status);
    }

    [Fact]
    public void Dispose_cancels_the_current_token()
    {
        var (vm, registry, _, _) = Build();
        var token = Token(vm.Link);

        vm.Dispose();

        Assert.Equal(PairStatus.Invalid, registry.TryPair(token, "Phone").Status);
    }

    [Fact]
    public void Expiry_cancels_the_token_even_before_the_registry_would_drop_it()
    {
        var (vm, registry, _, clock) = Build();
        var token = Token(vm.Link);
        clock.Advance(PhoneRegistry.PairingTokenLife);
        vm.Tick();

        Assert.True(vm.IsExpired);
        Assert.False(registry.CancelPairingToken(token));
    }

    [Fact]
    public void Cancelling_a_token_that_was_already_used_is_harmless()
    {
        var (vm, registry, _, _) = Build();
        Assert.Equal(PairStatus.Paired, registry.TryPair(Token(vm.Link), "Phone").Status);

        vm.Regenerate();
        vm.Dispose();

        Assert.Single(registry.List());
    }

    [Fact]
    public void A_link_that_can_no_longer_work_is_withdrawn_on_the_next_tick()
    {
        var (vm, registry, links, _) = Build();
        var token = Token(vm.Link);

        links.Available = false;
        vm.Tick();

        Assert.Equal(string.Empty, vm.Link);
        Assert.Empty(vm.QrPng);
        Assert.Equal("Tailscale is not installed.", vm.Message);
        Assert.Equal(PairStatus.Invalid, registry.TryPair(token, "Phone").Status);
    }

    [Fact]
    public void The_expiry_text_counts_down_and_the_link_is_replaced_by_the_notice_after_five_minutes()
    {
        var (vm, _, _, clock) = Build();

        clock.Advance(TimeSpan.FromSeconds(75));
        vm.Tick();
        Assert.Equal("Expires in 3:45", vm.ExpiresText);
        Assert.False(vm.IsExpired);

        clock.Advance(TimeSpan.FromSeconds(225));
        vm.Tick();

        Assert.True(vm.IsExpired);
        Assert.Equal("This link expired. Create a new one.", vm.Message);
        Assert.Equal(string.Empty, vm.Link);
        Assert.Empty(vm.QrPng);
        Assert.False(vm.HasLink);
    }

    [Fact]
    public void Regenerate_after_expiry_starts_again()
    {
        var (vm, _, _, clock) = Build();
        clock.Advance(TimeSpan.FromMinutes(6));
        vm.Tick();

        vm.Regenerate();

        Assert.False(vm.IsExpired);
        Assert.True(vm.HasLink);
        Assert.Equal(string.Empty, vm.Message);
        Assert.Equal("Expires in 5:00", vm.ExpiresText);
    }

    [Fact]
    public void Without_a_link_the_tailscale_sentence_is_shown_instead_of_a_qr_code()
    {
        var (vm, _, _, _) = Build(available: false);

        Assert.Equal("Tailscale is not installed.", vm.Message);
        Assert.Equal(string.Empty, vm.Link);
        Assert.Empty(vm.QrPng);
        Assert.False(vm.IsExpired);
    }

    [Fact]
    public void Devices_mirror_the_registry_and_a_pairing_adds_a_row()
    {
        var (vm, registry, _, _) = Build();
        Assert.Empty(vm.Devices);
        Assert.True(vm.HasNoDevices);

        registry.TryPair(Token(vm.Link), "My phone");

        var row = Assert.Single(vm.Devices);
        Assert.Equal("My phone", row.Name);
        Assert.StartsWith("Paired 2026-11-", row.Created);
        Assert.StartsWith("Last seen 2026-11-", row.LastSeen);
        Assert.True(vm.HasDevices);
    }

    [Fact]
    public void The_refresh_goes_through_the_post_function()
    {
        var posted = new List<Action>();
        var (vm, registry, _, _) = Build(post: posted.Add);

        registry.TryPair(Token(vm.Link), "Phone");
        Assert.Empty(vm.Devices);

        posted.ForEach(a => a());
        Assert.Single(vm.Devices);
    }

    [Fact]
    public void Revoke_and_revoke_all_call_the_registry_and_the_rows_disappear()
    {
        var (vm, registry, _, _) = Build();
        registry.TryPair(Token(vm.Link), "One");
        vm.Regenerate();
        registry.TryPair(Token(vm.Link), "Two");
        vm.Regenerate();
        registry.TryPair(Token(vm.Link), "Three");
        Assert.Equal(3, vm.Devices.Count);

        vm.Devices[0].RevokeCommand.Execute(null);
        Assert.Equal(2, vm.Devices.Count);
        Assert.Equal(2, registry.List().Count);

        vm.Revoke(vm.Devices[0].Id);
        Assert.Single(vm.Devices);

        vm.RevokeAll();
        Assert.Empty(vm.Devices);
        Assert.Empty(registry.List());
    }

    [Fact]
    public void After_Dispose_a_change_of_devices_does_not_refresh()
    {
        var (vm, registry, _, _) = Build();
        var token = registry.CreatePairingToken();

        vm.Dispose();
        registry.TryPair(token, "Phone");

        Assert.Empty(vm.Devices);
    }

    [Fact]
    public void A_queued_refresh_after_Dispose_does_nothing()
    {
        var posted = new List<Action>();
        var (vm, registry, _, _) = Build(post: posted.Add);
        registry.TryPair(Token(vm.Link), "Phone");

        vm.Dispose();
        posted.ForEach(a => a());

        Assert.Empty(vm.Devices);
    }

    [Fact]
    public async Task Copy_link_puts_the_link_on_the_clipboard()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(), clock.GetUtcNow);
        string? copied = null;
        var vm = new PairingViewModel(new FakeLinks(registry), registry, clock.GetUtcNow, null, t => { copied = t; return Task.CompletedTask; });

        await vm.CopyLinkCommand.ExecuteAsync(null);

        Assert.Equal(vm.Link, copied);
    }
}
