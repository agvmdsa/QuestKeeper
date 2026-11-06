using AqwCompanion.App.Account;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Account;

public sealed class AccountDataPersistenceTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccountSummary Summary = new(100, "Free", "Not a member", 7);
    private static readonly CharacterBadges Badges = new([new Badge("Fake Badge", "A fake description", "Fake Category")]);

    private readonly InMemoryStoredDataStore _store = new();

    private AccountDataPersistence Build() => new(_store);

    private static CharacterEquipment Equipment(string? name) =>
        new(name, 10, null, null, null, "Fake Class", null,
            [new EquipmentSlot("Weapon", "Fake Sword", 1, false)], [new CharacterColor("Skin", 0x112233)], 42);

    [Fact]
    public void Restore_returns_null_when_no_identity_is_stored()
    {
        Assert.Null(Build().Restore());
    }

    [Fact]
    public void Saved_identity_summary_equipment_and_badges_are_restored_together()
    {
        var sut = Build();
        var equipmentAt = At.AddMinutes(1);
        var badgesAt = At.AddMinutes(2);

        sut.SaveIdentity("Fakehero", Summary, At);
        sut.SaveEquipment(Equipment("FAKEHERO"), equipmentAt);
        sut.SaveBadges(Badges, badgesAt);

        var restored = Assert.IsType<RestoredAccount>(sut.Restore());
        Assert.Equal("Fakehero", restored.CharacterName);
        Assert.Equal(Summary, restored.Summary);
        Assert.Equal(At, restored.IdentityReadAtUtc);
        Assert.Equal("FAKEHERO", restored.Equipment?.Name);
        Assert.Equal(equipmentAt, restored.EquipmentReadAtUtc);
        Assert.Equal(Badges, restored.Badges);
        Assert.Equal(badgesAt, restored.BadgesReadAtUtc);
    }

    [Fact]
    public void SaveIdentity_with_null_summary_removes_the_stored_summary()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", Summary, At);
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.SummaryKey));

        sut.SaveIdentity("Fakehero", null, At);

        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.SummaryKey));
        Assert.Null(sut.Restore()?.Summary);
    }

    [Fact]
    public void SaveIdentity_for_another_character_removes_equipment_and_badges_stored_on_disk()
    {
        var sut = Build();
        sut.SaveIdentity("Hero A", Summary, At);
        sut.SaveEquipment(Equipment("Hero A"), At);
        sut.SaveBadges(Badges, At);

        // A fresh coordinator: nothing was restored in memory.
        Build().SaveIdentity("Hero B", Summary, At);

        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.EquipmentKey));
        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.BadgesKey));
        Assert.Equal("Hero B", Assert.IsType<RestoredAccount>(sut.Restore()).CharacterName);
    }

    [Fact]
    public void SaveIdentity_for_the_same_character_keeps_equipment_and_badges()
    {
        var sut = Build();
        sut.SaveIdentity("Hero A", Summary, At);
        sut.SaveEquipment(Equipment("Hero A"), At);
        sut.SaveBadges(Badges, At);

        sut.SaveIdentity("HERO a", Summary, At.AddMinutes(5));

        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.EquipmentKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.BadgesKey));
    }

    [Fact]
    public void SaveIdentity_without_a_stored_identity_removes_orphan_equipment_and_badges()
    {
        var sut = Build();
        sut.SaveEquipment(Equipment("Hero A"), At);
        sut.SaveBadges(Badges, At);

        sut.SaveIdentity("Hero B", Summary, At);

        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.EquipmentKey));
        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.BadgesKey));
    }

    [Fact]
    public void Equipment_for_another_character_is_ignored_on_restore()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", Summary, At);
        sut.SaveEquipment(Equipment("Someoneelse"), At);
        sut.SaveBadges(Badges, At);

        var restored = Assert.IsType<RestoredAccount>(sut.Restore());
        Assert.Null(restored.Equipment);
        Assert.Null(restored.EquipmentReadAtUtc);
        Assert.Null(restored.Badges);
        Assert.Null(restored.BadgesReadAtUtc);
        Assert.Equal(Summary, restored.Summary);
    }

    [Fact]
    public void Equipment_with_no_name_is_accepted()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", null, At);
        sut.SaveEquipment(Equipment(null), At);
        sut.SaveBadges(Badges, At);

        var restored = Assert.IsType<RestoredAccount>(sut.Restore());
        Assert.NotNull(restored.Equipment);
        Assert.NotNull(restored.Badges);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_stored_name_is_treated_as_no_identity(string name)
    {
        var sut = Build();
        sut.SaveIdentity(name, Summary, At);
        sut.SaveEquipment(Equipment(null), At);

        Assert.Null(sut.Restore());
    }

    [Fact]
    public void RemoveEquipment_and_RemoveBadges_only_remove_their_own_key()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", Summary, At);
        sut.SaveEquipment(Equipment("Fakehero"), At);
        sut.SaveBadges(Badges, At);

        sut.RemoveBadges();
        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.BadgesKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.EquipmentKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.IdentityKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.SummaryKey));

        sut.SaveBadges(Badges, At);
        sut.RemoveEquipment();
        Assert.False(_store.Items.ContainsKey(AccountDataPersistence.EquipmentKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.BadgesKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.IdentityKey));
        Assert.True(_store.Items.ContainsKey(AccountDataPersistence.SummaryKey));
    }

    [Fact]
    public void Forget_clears_the_store()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", Summary, At);
        sut.SaveBadges(Badges, At);

        Assert.True(sut.Forget());

        Assert.Equal(1, _store.ClearCalls);
        Assert.Empty(_store.Items);
        Assert.Null(sut.Restore());
    }

    [Fact]
    public void Forget_reports_failure_when_the_store_cannot_clear()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", Summary, At);

        _store.FailNext = true;

        Assert.False(sut.Forget());
    }

    [Fact]
    public void Forget_reports_failure_when_the_store_says_something_was_left()
    {
        _store.ClearResult = false;

        Assert.False(Build().Forget());
    }

    [Fact]
    public void Store_failures_are_swallowed()
    {
        var sut = Build();
        sut.SaveIdentity("Fakehero", Summary, At);

        _store.ThrowOnSave = true;
        _store.ThrowOnLoad = true;

        var ex = Record.Exception(() =>
        {
            sut.SaveIdentity("Other", Summary, At);
            sut.SaveEquipment(Equipment("Other"), At);
            sut.SaveBadges(Badges, At);
            sut.RemoveEquipment();
            sut.RemoveBadges();
            sut.Forget();
        });
        Assert.Null(ex);
        Assert.True(_store.SaveCalls > 1);
        Assert.Null(sut.Restore());

        _store.FailNext = true;
        Assert.Null(Record.Exception(() => sut.RemoveBadges()));
    }
}
