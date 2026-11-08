using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Inventory;

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
        Assert.Null(Build().Load<StoredSample>("summary", 1));
    }

    [Fact]
    public void Round_trips_a_summary_with_its_read_time()
    {
        var store = Build();
        var summary = new StoredSample(9585, "LEGEND", "Membership Expired: Tuesday, January 1, 2030", 3);

        store.Save("summary", 1, summary, ReadAt);
        var loaded = store.Load<StoredSample>("summary", 1);

        Assert.NotNull(loaded);
        Assert.Equal(summary, loaded.Value);
        Assert.Equal(ReadAt, loaded.ReadAtUtc);
    }

    [Fact]
    public void Round_trips_the_inventory_without_its_computed_properties()
    {
        var store = Build();
        var inventory = new CharacterInventory(
            [new InventoryItem("Test Sword", "Sword", 1, true, false), new InventoryItem("Test Ore", "Resource", 250, false, true)]);

        store.Save("inventory", 1, inventory, ReadAt);

        var loaded = store.Load<CharacterInventory>("inventory", 1)!.Value;
        Assert.Equal(inventory.Items, loaded.Items);
        Assert.Equal(250, loaded.CountOf("test ore"));
        var json = System.Text.Json.JsonSerializer.Serialize(inventory, StoredJson.Options);
        Assert.DoesNotContain("typeCounts", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_again_replaces_the_value()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
        store.Save("summary", 1, new StoredSample(2, null, null, null), ReadAt.AddMinutes(5));

        var loaded = store.Load<StoredSample>("summary", 1)!;
        Assert.Equal(2, loaded.Value.Amount);
        Assert.Equal(ReadAt.AddMinutes(5), loaded.ReadAtUtc);
    }

    [Fact]
    public void Remove_deletes_only_that_key()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
        store.Save("badges", 1, new StoredSample(null, null, null, null), ReadAt);

        store.Remove("summary");

        Assert.Null(store.Load<StoredSample>("summary", 1));
        Assert.NotNull(store.Load<StoredSample>("badges", 1));
    }

    [Fact]
    public void Clear_deletes_the_database_file_and_the_store_still_works_afterwards()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
        Assert.True(File.Exists(DatabaseFile));

        store.Clear();

        Assert.False(File.Exists(DatabaseFile));
        Assert.Null(store.Load<StoredSample>("summary", 1));
        store.Save("summary", 1, new StoredSample(5, null, null, null), ReadAt);
        Assert.Equal(5, store.Load<StoredSample>("summary", 1)!.Value.Amount);
    }

    [Fact]
    public void Unreadable_payload_is_treated_as_absent()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);

        // The stored summary is an object; asking for a list cannot deserialize and must not throw.
        Assert.Null(store.Load<List<int>>("summary", 1));
    }

    [Fact]
    public void Unknown_schema_version_is_treated_as_absent()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
        LiteDbStoredDataStoreTestHooks.SetSchemaVersion(DatabaseFile, "summary", 999);

        Assert.Null(store.Load<StoredSample>("summary", 1));
    }

    [Fact]
    public void A_record_written_with_schema_version_1_by_the_old_shape_still_loads_at_version_1()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        using (var db = new LiteDB.LiteDatabase(new LiteDB.ConnectionString { Filename = DatabaseFile, Connection = LiteDB.ConnectionType.Shared }))
        {
            db.GetCollection<StoredRecord>("records").Upsert(new StoredRecord
            {
                Id = "summary",
                SchemaVersion = 1,
                ReadAtUnixMs = ReadAt.ToUnixTimeMilliseconds(),
                Payload = System.Text.Json.JsonSerializer.Serialize(new StoredSample(42, null, null, null), StoredJson.Options),
            });
        }

        var loaded = Build().Load<StoredSample>("summary", 1);

        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.Value.Amount);
        Assert.Equal(ReadAt, loaded.ReadAtUtc);
    }

    [Fact]
    public void The_version_is_per_record_so_a_mismatch_drops_only_that_record()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
        store.Save("badges", 1, new StoredSample(null, null, null, null), ReadAt);

        Assert.Null(store.Load<StoredSample>("summary", 2));
        Assert.NotNull(store.Load<StoredSample>("badges", 1));
        Assert.NotNull(store.Load<StoredSample>("summary", 1));
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_a_fresh_file_is_used()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        File.WriteAllBytes(DatabaseFile, Enumerable.Range(0, 20000).Select(i => (byte)(i * 31 % 251)).ToArray());
        var store = Build();

        Assert.Null(store.Load<StoredSample>("summary", 1));
        Assert.True(File.Exists(DatabaseFile + ".corrupt"));

        store.Save("summary", 1, new StoredSample(7, null, null, null), ReadAt);
        Assert.Equal(7, store.Load<StoredSample>("summary", 1)!.Value.Amount);
    }

    [Fact]
    public void Corrupt_file_quarantine_also_removes_a_stale_log_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        File.WriteAllBytes(DatabaseFile, Enumerable.Range(0, 20000).Select(i => (byte)(i * 31 % 251)).ToArray());
        var log = Path.Combine(Path.GetDirectoryName(DatabaseFile)!, "companion-log.db");
        File.WriteAllBytes(log, [1, 2, 3]);

        Assert.Null(Build().Load<StoredSample>("summary", 1));

        Assert.False(File.Exists(log));
    }

    [Fact]
    public void A_bad_read_time_makes_only_that_record_absent()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
        store.Save("badges", 1, new StoredSample(null, null, null, null), ReadAt);
        LiteDbStoredDataStoreTestHooks.SetReadAt(DatabaseFile, "summary", long.MaxValue);

        Assert.Null(store.Load<StoredSample>("summary", 1));
        Assert.NotNull(store.Load<StoredSample>("badges", 1));
        Assert.False(File.Exists(DatabaseFile + ".corrupt"));
    }

    [Fact]
    public void An_exception_from_the_operation_never_quarantines_a_healthy_file()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);

        // Serializing the value throws inside the operation, after the database has been opened.
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => store.Save("summary", 1, new ThrowingValue(), ReadAt));

        Assert.False(File.Exists(DatabaseFile + ".corrupt"));
        Assert.Equal(1, store.Load<StoredSample>("summary", 1)!.Value.Amount);
    }

    [Fact]
    public void Clear_reports_success_and_removes_the_log_and_quarantined_files()
    {
        var store = Build();
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
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
        store.Save("summary", 1, new StoredSample(1, null, null, null), ReadAt);
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

    private sealed record StoredSample(int? Amount, string? Label, string? Note, int? Extra);
}
