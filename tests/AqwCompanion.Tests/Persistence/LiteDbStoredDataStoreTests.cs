using AqwCompanion.App.Persistence;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Equipment;

namespace AqwCompanion.Tests.Persistence;

public sealed class LiteDbStoredDataStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aqw-store-" + Guid.NewGuid().ToString("N"));
    private string DatabaseFile => Path.Combine(_directory, "data", "companion.db");

    private LiteDbStoredDataStore Build() => new(DatabaseFile);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static readonly DateTimeOffset ReadAt = new(2026, 11, 6, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Load_returns_null_when_nothing_was_saved()
    {
        Assert.Null(Build().Load<AccountSummary>("summary"));
    }

    [Fact]
    public void Round_trips_a_summary_with_its_read_time()
    {
        var store = Build();
        var summary = new AccountSummary(9585, "LEGEND", "Membership Expired: Tuesday, January 1, 2030", 3);

        store.Save("summary", summary, ReadAt);
        var loaded = store.Load<AccountSummary>("summary");

        Assert.NotNull(loaded);
        Assert.Equal(summary, loaded.Value);
        Assert.Equal(ReadAt, loaded.ReadAtUtc);
    }

    [Fact]
    public void Round_trips_equipment_and_badges()
    {
        var store = Build();
        var equipment = new CharacterEquipment(
            "Test Hero", 50, "Good", null, "M", "Warrior", null,
            [new EquipmentSlot("Armor", "Plate", 1, false), new EquipmentSlot("Armor", "Cosmetic", null, true)],
            [new CharacterColor("Skin", 0xFFAA00)],
            3);
        var badges = new CharacterBadges([new Badge("First", "Did a thing", "Quests"), new Badge("Second", "", "Other")]);

        store.Save("equipment", equipment, ReadAt);
        store.Save("badges", badges, ReadAt);

        Assert.Equal(equipment, store.Load<CharacterEquipment>("equipment")!.Value, new EquipmentComparer());
        Assert.Equal(3, store.Load<CharacterEquipment>("equipment")!.Value.CharacterId);
        Assert.Equal(2, store.Load<CharacterBadges>("badges")!.Value.Count);
        Assert.Equal("Quests", store.Load<CharacterBadges>("badges")!.Value.Badges[0].Category);
    }

    [Fact]
    public void Computed_properties_are_not_written_into_the_payload()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new CharacterBadges([new Badge("First", "d", "Quests")]), StoredJson.Options);

        Assert.DoesNotContain("groupedByCategory", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preview", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("badges", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_again_replaces_the_value()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        store.Save("summary", new AccountSummary(2, null, null, null), ReadAt.AddMinutes(5));

        var loaded = store.Load<AccountSummary>("summary")!;
        Assert.Equal(2, loaded.Value.AdventureCoins);
        Assert.Equal(ReadAt.AddMinutes(5), loaded.ReadAtUtc);
    }

    [Fact]
    public void Remove_deletes_only_that_key()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        store.Save("badges", new CharacterBadges([]), ReadAt);

        store.Remove("summary");

        Assert.Null(store.Load<AccountSummary>("summary"));
        Assert.NotNull(store.Load<CharacterBadges>("badges"));
    }

    [Fact]
    public void Clear_deletes_the_database_file_and_the_store_still_works_afterwards()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        Assert.True(File.Exists(DatabaseFile));

        store.Clear();

        Assert.False(File.Exists(DatabaseFile));
        Assert.Null(store.Load<AccountSummary>("summary"));
        store.Save("summary", new AccountSummary(5, null, null, null), ReadAt);
        Assert.Equal(5, store.Load<AccountSummary>("summary")!.Value.AdventureCoins);
    }

    [Fact]
    public void Unreadable_payload_is_treated_as_absent()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);

        // The stored summary is an object; asking for a list cannot deserialize and must not throw.
        Assert.Null(store.Load<List<int>>("summary"));
    }

    [Fact]
    public void Unknown_schema_version_is_treated_as_absent()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        LiteDbStoredDataStoreTestHooks.SetSchemaVersion(DatabaseFile, "summary", 999);

        Assert.Null(store.Load<AccountSummary>("summary"));
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_a_fresh_file_is_used()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        File.WriteAllBytes(DatabaseFile, Enumerable.Range(0, 20000).Select(i => (byte)(i * 31 % 251)).ToArray());
        var store = Build();

        Assert.Null(store.Load<AccountSummary>("summary"));
        Assert.True(File.Exists(DatabaseFile + ".corrupt"));

        store.Save("summary", new AccountSummary(7, null, null, null), ReadAt);
        Assert.Equal(7, store.Load<AccountSummary>("summary")!.Value.AdventureCoins);
    }

    [Fact]
    public void Corrupt_file_quarantine_also_removes_a_stale_log_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        File.WriteAllBytes(DatabaseFile, Enumerable.Range(0, 20000).Select(i => (byte)(i * 31 % 251)).ToArray());
        var log = Path.Combine(Path.GetDirectoryName(DatabaseFile)!, "companion-log.db");
        File.WriteAllBytes(log, [1, 2, 3]);

        Assert.Null(Build().Load<AccountSummary>("summary"));

        Assert.False(File.Exists(log));
    }

    [Fact]
    public void A_bad_read_time_makes_only_that_record_absent()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        store.Save("badges", new CharacterBadges([]), ReadAt);
        LiteDbStoredDataStoreTestHooks.SetReadAt(DatabaseFile, "summary", long.MaxValue);

        Assert.Null(store.Load<AccountSummary>("summary"));
        Assert.NotNull(store.Load<CharacterBadges>("badges"));
        Assert.False(File.Exists(DatabaseFile + ".corrupt"));
    }

    [Fact]
    public void An_exception_from_the_operation_never_quarantines_a_healthy_file()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);

        // Serializing the value throws inside the operation, after the database has been opened.
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => store.Save("summary", new ThrowingValue(), ReadAt));

        Assert.False(File.Exists(DatabaseFile + ".corrupt"));
        Assert.Equal(1, store.Load<AccountSummary>("summary")!.Value.AdventureCoins);
    }

    [Fact]
    public void Clear_reports_success_and_removes_the_log_and_quarantined_files()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        var directory = Path.GetDirectoryName(DatabaseFile)!;
        var log = Path.Combine(directory, "companion-log.db");
        File.WriteAllBytes(log, [1]);
        File.WriteAllBytes(DatabaseFile + ".corrupt", [2]);

        Assert.True(store.Clear());

        Assert.False(File.Exists(DatabaseFile));
        Assert.False(File.Exists(log));
        Assert.False(File.Exists(DatabaseFile + ".corrupt"));
    }

    [Fact]
    public void Clear_returns_true_when_nothing_exists()
    {
        Assert.True(Build().Clear());
    }

    [Fact]
    public void Clear_reports_failure_but_still_removes_the_other_files()
    {
        var store = Build();
        store.Save("summary", new AccountSummary(1, null, null, null), ReadAt);
        var directory = Path.GetDirectoryName(DatabaseFile)!;
        var log = Path.Combine(directory, "companion-log.db");
        File.WriteAllBytes(log, [1]);
        // A directory in place of the quarantine file cannot be deleted with File.Delete.
        Directory.CreateDirectory(DatabaseFile + ".corrupt");
        File.WriteAllBytes(Path.Combine(DatabaseFile + ".corrupt", "x"), [1]);

        Assert.False(store.Clear());

        Assert.False(File.Exists(DatabaseFile));
        Assert.False(File.Exists(log));
    }

    private sealed class ThrowingValue
    {
        public int Number { get => throw new ArgumentOutOfRangeException(nameof(Number)); set { } }
    }

    private sealed class EquipmentComparer : IEqualityComparer<CharacterEquipment>
    {
        public bool Equals(CharacterEquipment? x, CharacterEquipment? y) =>
            x is not null && y is not null
            && x.Name == y.Name && x.Level == y.Level && x.Faction == y.Faction && x.ClassName == y.ClassName
            && x.Slots.SequenceEqual(y.Slots) && x.Colors.SequenceEqual(y.Colors);

        public int GetHashCode(CharacterEquipment obj) => obj.Name?.GetHashCode() ?? 0;
    }
}
