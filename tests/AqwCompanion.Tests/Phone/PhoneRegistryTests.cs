using System.Text.RegularExpressions;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneRegistryTests
{
    private static (PhoneRegistry Registry, FakeClock Clock, MemoryPhoneFileStore Store) Build()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        var store = new MemoryPhoneFileStore();
        return (new PhoneRegistry(store, clock.Read), clock, store);
    }

    private static string PairDevice(PhoneRegistry registry, string? name = "Phone")
    {
        var result = registry.TryPair(registry.CreatePairingToken(), name);
        Assert.Equal(PairStatus.Paired, result.Status);
        return result.DeviceToken!;
    }

    [Fact]
    public void FreshRegistryIsDisabledAndEnabledPersists()
    {
        var (registry, clock, store) = Build();
        Assert.False(registry.Enabled);

        registry.SetEnabled(true);

        Assert.Equal(1, store.Saves);
        Assert.True(new PhoneRegistry(store, clock.Read).Enabled);
    }

    [Fact]
    public void PairingTokenIs43UrlSafeCharsAndUnique()
    {
        var (registry, _, _) = Build();
        var a = registry.CreatePairingToken();
        var b = registry.CreatePairingToken();

        Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GoodTokenPairsOnceOnly()
    {
        var (registry, _, _) = Build();
        var token = registry.CreatePairingToken();

        var first = registry.TryPair(token, "Phone");
        var second = registry.TryPair(token, "Phone");

        Assert.Equal(PairStatus.Paired, first.Status);
        Assert.Equal(43, first.DeviceToken!.Length);
        Assert.Equal(PairStatus.Invalid, second.Status);
        Assert.Null(second.DeviceToken);
    }

    [Fact]
    public void TokenStillPairsJustBeforeFiveMinutes()
    {
        var (registry, clock, _) = Build();
        var token = registry.CreatePairingToken();
        clock.Advance(TimeSpan.FromSeconds(299));

        Assert.Equal(PairStatus.Paired, registry.TryPair(token, "x").Status);
    }

    [Fact]
    public void TokenIsInvalidAtFiveMinutes()
    {
        var (registry, clock, _) = Build();
        var token = registry.CreatePairingToken();
        clock.Advance(TimeSpan.FromSeconds(300));

        Assert.Equal(PairStatus.Invalid, registry.TryPair(token, "x").Status);
    }

    [Fact]
    public void OnlyFiveTokensStayOutstanding()
    {
        var (registry, clock, _) = Build();
        var tokens = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            tokens.Add(registry.CreatePairingToken());
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(PairStatus.Invalid, registry.TryPair(tokens[0], "x").Status);
        Assert.Equal(PairStatus.Paired, registry.TryPair(tokens[5], "x").Status);
    }

    [Fact]
    public void FiveFailuresLockEvenAGoodTokenUntilAMinutePasses()
    {
        var (registry, clock, _) = Build();
        var good = registry.CreatePairingToken();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(PairStatus.Invalid, registry.TryPair("wrong" + i, "x").Status);
        }

        Assert.Equal(PairStatus.Locked, registry.TryPair(good, "x").Status);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PairStatus.Paired, registry.TryPair(good, "x").Status);
    }

    [Fact]
    public void SavedFileHoldsTheHashNotTheToken()
    {
        var (registry, _, store) = Build();
        var deviceToken = PairDevice(registry);

        var json = PhoneFileJson.Serialize(store.Saved);

        Assert.DoesNotContain(deviceToken, json);
        var device = Assert.Single(store.Saved.Devices);
        Assert.Matches(new Regex("^[0-9A-F]{64}$"), device.TokenHash);
        Assert.Contains(device.TokenHash, json);
    }

    [Fact]
    public void AuthenticateAcceptsTheRightTokenOnlyAndPersists()
    {
        var (registry, clock, store) = Build();
        var token = PairDevice(registry, "Pixel");

        var device = registry.Authenticate(token);
        Assert.NotNull(device);
        Assert.Equal("Pixel", device.Name);
        Assert.Null(registry.Authenticate("wrong"));
        Assert.Null(registry.Authenticate(null));
        Assert.Null(registry.Authenticate(""));

        var second = new PhoneRegistry(store, clock.Read);
        Assert.Equal(device.Id, second.Authenticate(token)?.Id);
    }

    [Fact]
    public void AuthenticateSavesLastSeenAtMostOncePerMinute()
    {
        var (registry, clock, store) = Build();
        var token = PairDevice(registry);

        clock.Advance(TimeSpan.FromSeconds(61));
        registry.Authenticate(token);
        var afterFirst = store.Saves;

        clock.Advance(TimeSpan.FromSeconds(30));
        registry.Authenticate(token);
        Assert.Equal(afterFirst, store.Saves);

        clock.Advance(TimeSpan.FromSeconds(61));
        registry.Authenticate(token);
        Assert.Equal(afterFirst + 1, store.Saves);
    }

    [Fact]
    public void RevokeRemovesOneDeviceAndRevokeAllRemovesTheRest()
    {
        var (registry, _, _) = Build();
        var tokenA = PairDevice(registry, "A");
        var tokenB = PairDevice(registry, "B");
        var idA = registry.Authenticate(tokenA)!.Id;

        Assert.True(registry.Revoke(idA));
        Assert.False(registry.Revoke("nope"));
        Assert.Null(registry.Authenticate(tokenA));
        Assert.NotNull(registry.Authenticate(tokenB));

        PairDevice(registry, "C");
        Assert.Equal(2, registry.RevokeAll());
        Assert.Empty(registry.List());
        Assert.Null(registry.Authenticate(tokenB));
    }

    [Fact]
    public void DeviceNamesAreCleaned()
    {
        var (registry, _, _) = Build();
        PairDevice(registry, "  Pixel\u0007 7  ");
        PairDevice(registry, new string('a', 100));
        PairDevice(registry, null);

        var names = registry.List().Select(d => d.Name).ToList();
        Assert.Equal("Pixel 7", names[0]);
        Assert.Equal(new string('a', 40), names[1]);
        Assert.Equal("Browser", names[2]);
    }

    [Fact]
    public void FileJsonRoundTripsAndRejectsGarbage()
    {
        var t = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var file = new PhoneFile(true, [new StoredDevice("id1", "Phone", "ABCD", t, t.AddMinutes(3))]);

        Assert.True(PhoneFileJson.TryParse(PhoneFileJson.Serialize(file), out var back));
        Assert.True(back.Enabled);
        Assert.Equal(file.Devices.Single(), back.Devices.Single());

        Assert.False(PhoneFileJson.TryParse("{", out _));
        Assert.True(PhoneFileJson.TryParsePair("{\"token\":\"a\",\"name\":\"b\"}", out var pair));
        Assert.Equal(new PairBody("a", "b"), pair);
        Assert.False(PhoneFileJson.TryParsePair("[]", out _));
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"enabled\":true}", true)]
    public void FileJsonWithoutDevicesGivesAnEmptyList(string json, bool enabled)
    {
        Assert.True(PhoneFileJson.TryParse(json, out var file));
        Assert.Equal(enabled, file.Enabled);
        Assert.Empty(file.Devices);
    }

    [Fact]
    public void RegistryToleratesAFileWithNullDevices()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        var registry = new PhoneRegistry(new MemoryPhoneFileStore(new PhoneFile(true, null!)), clock.Read);

        Assert.True(registry.Enabled);
        Assert.Empty(registry.List());
    }

    [Fact]
    public void FormatCharactersAreStrippedFromNames()
    {
        var (registry, _, _) = Build();
        PairDevice(registry, "Pi\u202Exel");

        Assert.Equal("Pixel", registry.List().Single().Name);
    }

    [Fact]
    public void CuttingANameDoesNotSplitASurrogatePair()
    {
        var (registry, _, _) = Build();
        PairDevice(registry, new string('a', 39) + "\U0001F600" + "zz");

        Assert.Equal(new string('a', 39), registry.List().Single().Name);
    }

    private static (List<IReadOnlyList<string>> Revoked, Func<int> ChangedCount) Watch(PhoneRegistry registry)
    {
        var revoked = new List<IReadOnlyList<string>>();
        var changed = 0;
        registry.DevicesRevoked += revoked.Add;
        registry.DevicesChanged += () => changed++;
        return (revoked, () => changed);
    }

    [Fact]
    public void RevokeRaisesDevicesRevokedOnceWithThatId()
    {
        var (registry, _, _) = Build();
        var tokenA = PairDevice(registry, "A");
        PairDevice(registry, "B");
        var idA = registry.Authenticate(tokenA)!.Id;
        var (revoked, _) = Watch(registry);

        Assert.True(registry.Revoke(idA));

        Assert.Equal([idA], Assert.Single(revoked));
    }

    [Fact]
    public void RevokeAllRaisesDevicesRevokedOnceWithAllIds()
    {
        var (registry, _, _) = Build();
        PairDevice(registry, "A");
        PairDevice(registry, "B");
        var ids = registry.List().Select(d => d.Id).ToList();
        var (revoked, _) = Watch(registry);

        Assert.Equal(2, registry.RevokeAll());

        Assert.Equal(ids.Order(), Assert.Single(revoked).Order());
    }

    [Fact]
    public void RevokingAnUnknownIdOrNothingRaisesNothing()
    {
        var (registry, _, _) = Build();
        var (revoked, changed) = Watch(registry);

        Assert.False(registry.Revoke("nope"));
        Assert.Equal(0, registry.RevokeAll());

        Assert.Empty(revoked);
        Assert.Equal(0, changed());
    }

    [Fact]
    public void DevicesChangedIsRaisedOnceOnASuccessfulPair()
    {
        var (registry, _, _) = Build();
        var (_, changed) = Watch(registry);

        PairDevice(registry);

        Assert.Equal(1, changed());
    }

    [Fact]
    public void DevicesChangedIsNotRaisedOnAFailedPairing()
    {
        var (registry, _, _) = Build();
        var (_, changed) = Watch(registry);

        Assert.Equal(PairStatus.Invalid, registry.TryPair("wrong", "x").Status);
        for (var i = 0; i < 5; i++)
        {
            registry.TryPair("wrong" + i, "x");
        }

        Assert.Equal(PairStatus.Locked, registry.TryPair(registry.CreatePairingToken(), "x").Status);
        Assert.Equal(0, changed());
    }

    [Fact]
    public void DevicesChangedIsRaisedOnceOnRevokeAndOnceOnRevokeAllWithDevices()
    {
        var (registry, _, _) = Build();
        var token = PairDevice(registry, "A");
        PairDevice(registry, "B");
        var id = registry.Authenticate(token)!.Id;
        var (_, changed) = Watch(registry);

        registry.Revoke(id);
        Assert.Equal(1, changed());

        registry.RevokeAll();
        Assert.Equal(2, changed());

        registry.RevokeAll();
        Assert.Equal(2, changed());
    }

    [Fact]
    public void HandlersRunOutsideTheLock()
    {
        var (registry, _, _) = Build();
        var seen = new List<int>();
        registry.DevicesChanged += () =>
        {
            // Another thread asking for the lock would deadlock if the handler still held it.
            var task = Task.Run(() => registry.List().Count);
            Assert.True(task.Wait(TimeSpan.FromSeconds(5)), "List() blocked: the handler ran inside the lock.");
            seen.Add(task.Result);
        };
        registry.DevicesRevoked += _ =>
        {
            var task = Task.Run(() => registry.List().Count);
            Assert.True(task.Wait(TimeSpan.FromSeconds(5)), "List() blocked: the handler ran inside the lock.");
            seen.Add(task.Result);
        };

        PairDevice(registry);
        registry.RevokeAll();

        Assert.Equal([1, 0, 0], seen);
    }

    [Fact]
    public void ThrowingHandlersAreSwallowed()
    {
        var (registry, _, _) = Build();
        registry.DevicesChanged += () => throw new InvalidOperationException("boom");
        registry.DevicesRevoked += _ => throw new InvalidOperationException("boom");

        var token = PairDevice(registry);

        Assert.NotNull(registry.Authenticate(token));
        Assert.Equal(1, registry.RevokeAll());
    }

    [Fact]
    public void ForgetRevokesAllDeletesTheFileOnceAndKeepsEnabled()
    {
        var (registry, _, store) = Build();
        registry.SetEnabled(true);
        var token = PairDevice(registry);
        var id = registry.List().Single().Id;
        var (revoked, changed) = Watch(registry);

        registry.Forget();

        Assert.Equal(1, store.Deletes);
        Assert.Empty(registry.List());
        Assert.Null(registry.Authenticate(token));
        Assert.True(registry.Enabled);
        Assert.Equal([id], Assert.Single(revoked));
        Assert.Equal(1, changed());
    }

    [Fact]
    public void ForgetRaisesDevicesChangedOnceEvenWithNoDevices()
    {
        var (registry, _, store) = Build();
        var (revoked, changed) = Watch(registry);

        registry.Forget();

        Assert.Equal(1, changed());
        Assert.Empty(revoked);
        Assert.Equal(1, store.Deletes);
    }

    [Fact]
    public void ForgetInvalidatesOutstandingPairingTokens()
    {
        var (registry, _, _) = Build();
        var token = registry.CreatePairingToken();

        registry.Forget();

        Assert.Equal(PairStatus.Invalid, registry.TryPair(token, "x").Status);
    }

    [Fact]
    public void ForgetClearsTheLockAndTheFailureCount()
    {
        var (registry, _, _) = Build();
        for (var i = 0; i < 5; i++)
        {
            registry.TryPair("wrong" + i, "x");
        }

        Assert.Equal(PairStatus.Locked, registry.TryPair("anything", "x").Status);

        registry.Forget();

        Assert.Equal(PairStatus.Paired, registry.TryPair(registry.CreatePairingToken(), "x").Status);

        // Failures counted before Forget do not add up with later ones.
        for (var i = 0; i < 4; i++)
        {
            registry.TryPair("w" + i, "x");
        }

        registry.Forget();
        registry.TryPair("one more", "x");
        Assert.Equal(PairStatus.Paired, registry.TryPair(registry.CreatePairingToken(), "x").Status);
    }
}
