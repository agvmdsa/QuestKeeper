using AqwCompanion.App.Reputation;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Reputation;

public class ReputationStoreTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private static StoredReputation Sample() => new(
        new ReputationSnapshot([new FactionStanding(1, "Good", 5, 1234, 5000, 7), new FactionStanding(2, "Evil", 1, 2, 3, 0)], "2026-11-07T15:00:00Z"),
        "reputation.json",
        At);

    [Fact]
    public void Saved_reputation_is_loaded_back_unchanged()
    {
        var store = new ReputationStore(new InMemoryStoredDataStore());

        store.Save(Sample());

        var loaded = store.Load()!;
        Assert.Equal(Sample().Snapshot.Factions, loaded.Snapshot.Factions);
        Assert.Equal("2026-11-07T15:00:00Z", loaded.Snapshot.ExportedAt);
        Assert.Equal("reputation.json", loaded.SourceName);
        Assert.Equal(At, loaded.ImportedAt);
    }

    [Fact]
    public void Nothing_stored_loads_as_null() =>
        Assert.Null(new ReputationStore(new InMemoryStoredDataStore()).Load());

    [Fact]
    public void The_data_lives_under_the_reputation_key()
    {
        var inner = new InMemoryStoredDataStore();

        new ReputationStore(inner).Save(Sample());

        Assert.Equal(["reputation.data"], inner.Items.Keys);
    }

    [Fact]
    public void Remove_deletes_the_stored_reputation()
    {
        var store = new ReputationStore(new InMemoryStoredDataStore());
        store.Save(Sample());

        store.Remove();

        Assert.Null(store.Load());
    }

    [Fact]
    public void Corrupt_stored_data_is_ignored()
    {
        var inner = new InMemoryStoredDataStore();
        inner.Items[ReputationStore.Key] = (new ReputationStore.ReputationDto(
            [
                null!,
                new(1, "", 1, 1, 1, 0),
                new(2, "Negative", -1, 1, 1, 0),
                new(3, "Fine", 1, 1, 1, 0),
                new(3, "Duplicate", 9, 9, 9, 9),
                new(-4, "Bad Id", 1, 1, 1, 0),
            ],
            null,
            null!), At);

        var loaded = new ReputationStore(inner).Load()!;

        Assert.Equal("Fine", Assert.Single(loaded.Snapshot.Factions).Name);
        Assert.Equal(string.Empty, loaded.SourceName);
    }

    [Fact]
    public void A_value_of_another_shape_or_with_no_usable_entries_loads_as_null()
    {
        var inner = new InMemoryStoredDataStore();
        inner.Items[ReputationStore.Key] = ("not a reputation", At);
        Assert.Null(new ReputationStore(inner).Load());

        inner.Items[ReputationStore.Key] = (new ReputationStore.ReputationDto(null!, null, "x"), At);
        Assert.Null(new ReputationStore(inner).Load());

        inner.Items[ReputationStore.Key] = (new ReputationStore.ReputationDto([], null, "x"), At);
        Assert.Null(new ReputationStore(inner).Load());
    }

    [Fact]
    public void Store_failures_are_swallowed()
    {
        var inner = new InMemoryStoredDataStore { ThrowOnLoad = true, ThrowOnSave = true };
        var store = new ReputationStore(inner);

        store.Save(Sample());
        inner.FailNext = true;
        store.Remove();

        Assert.Null(store.Load());
    }
}
