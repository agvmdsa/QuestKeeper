using AqwCompanion.App.Account;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Account;

public class AccountSessionDataTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 4, 5, 6, 0, TimeSpan.Zero);

    private readonly LatestResultStore<CharacterEquipment> _equipment = new();
    private readonly LatestResultStore<CharacterBadges> _badges = new();
    private readonly LatestResultStore<AccountIdentity> _identity = new();
    private readonly FailedSnapshotBuffer _failures = new();

    private AccountSessionData Build() => new(_equipment, _badges, _identity, _failures);

    private static CharacterBadges Badges(int count) =>
        new(Enumerable.Range(0, count).Select(i => new Badge("Badge " + i, "d", "Category")).ToList());

    private static CharacterEquipment Equipment(int slots) =>
        new("Hero", 10, null, null, null, "Class", null,
            Enumerable.Range(0, slots).Select(i => new EquipmentSlot("Armor", "Item " + i, 1, false)).ToList(),
            [], 3);

    private static RestoredAccount Restored(CharacterEquipment? equipment, CharacterBadges? badges) =>
        new("Hero", null, At, equipment, equipment is null ? null : At, badges, badges is null ? null : At);

    [Fact]
    public async Task A_degraded_first_badges_read_after_a_restore_is_suspicious_and_not_persisted()
    {
        Build().Seed(Restored(Equipment(12), Badges(300)));
        var parser = new FakeParser<CharacterBadges>(_ =>
            ParseResult<CharacterBadges>.Ok(Badges(12), new ParseDiagnostics("fake", "1", "s", [], 12)));
        var ingestion = new IngestionService<CharacterBadges>(parser, _badges, _failures, new ListLogger<IngestionService<CharacterBadges>>());
        var snapshot = PageSnapshot.Create("https://account.aq.com/AQW/Badges?x=1", "<html/>", "1.0.0", At);

        var result = await ingestion.IngestAsync(snapshot);

        Assert.Equal(ParseOutcome.Suspicious, result.Parse.Outcome);
        Assert.False(result.Persisted);
        Assert.Null(_badges.Latest);
        Assert.Equal(300, await _badges.GetLastCountAsync());
    }

    [Fact]
    public async Task Seed_sets_the_equipment_baseline_from_the_slot_count_and_skips_missing_parts()
    {
        Build().Seed(Restored(Equipment(14), null));

        Assert.Equal(14, await _equipment.GetLastCountAsync());
        Assert.Null(await _badges.GetLastCountAsync());
    }

    [Fact]
    public async Task Clear_empties_the_stores_and_the_failed_snapshot()
    {
        await _equipment.SaveAsync(Equipment(2), 2);
        await _badges.SaveAsync(Badges(2), 2);
        await _identity.SaveAsync(new AccountIdentity("Hero", null), 1);
        _failures.Record(PageSnapshot.Create("https://account.aq.com/AQW/Badges?x=1", "<html>secret</html>", "1.0.0", At));

        Build().Clear();

        Assert.Null(_equipment.Latest);
        Assert.Null(_badges.Latest);
        Assert.Null(_identity.Latest);
        Assert.Null(await _badges.GetLastCountAsync());
        Assert.Null(_failures.Latest);
    }
}
