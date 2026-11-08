using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Reputation;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Forgetting;

/// <summary>Proves by effect, on a real folder and a real database file, what Forgetting leaves behind.</summary>
public sealed class ForgettingEffectTests : IDisposable
{
    private static readonly DateTimeOffset ReadAt = new(2026, 11, 6, 12, 30, 0, TimeSpan.Zero);

    private readonly AppPaths _paths =
        new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Forgetting_leaves_nothing_but_logs_and_exports()
    {
        var world = await BuildWorldAsync(new LiteDbStoredDataStore(_paths.DatabasePath), gateFails: false);
        Assert.Contains("data/companion.db", FilesUnderRoot());
        Assert.Contains("reputation/reputation.json", FilesUnderRoot());

        var result = world.Coordinator.Forget();

        Assert.True(result.IsComplete);
        Assert.Equal(["exports/y.zip", "logs/x.txt"], FilesUnderRoot());
        Assert.Empty(world.HardFarms.Rows);
        Assert.True(world.HardFarms.ShowEmptyState);
        Assert.Empty(world.Reputation.Items);
        Assert.True(world.Reputation.IsEmpty);
        Assert.Empty(world.Session.Characters);
        Assert.Equal(CaptureState.Idle, world.Session.State);
        Assert.Equal(default, world.Session.Counters);
        Assert.Equal(CaptureState.Done, world.StateWhenStoredDataWasRemoved);
        Assert.True(world.SourceToken.IsCancellationRequested);
    }

    [Fact]
    public async Task A_failed_stored_data_removal_leaves_every_file_in_place()
    {
        var world = await BuildWorldAsync(new LiteDbStoredDataStore(_paths.DatabasePath), gateFails: true);
        var before = SnapshotFiles();

        var result = world.Coordinator.Forget();

        Assert.False(result.StoredDataRemoved);
        Assert.Equal(before.Keys, SnapshotFiles().Keys);
        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_paths.Root, path)));
        }

        Assert.Single(world.HardFarms.Rows);
        Assert.False(world.HardFarms.ShowEmptyState);
        Assert.Single(world.Reputation.Items);
        Assert.False(world.Reputation.IsEmpty);
        Assert.Equal("Fictional Hero", Assert.Single(world.Session.Characters).Profile.Name);
    }

    private async Task<World> BuildWorldAsync(LiteDbStoredDataStore store, bool gateFails)
    {
        var storedData = new StoredData(store, StoredKeys.Obsolete);

        var hardFarmStore = new HardFarmStore(new StoredData(store, StoredKeys.Obsolete));
        hardFarmStore.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), ReadAt);
        hardFarmStore.SaveIndex(new WikiIndex(WikiUrls.ClassesSlug, WikiType.Class, [new WikiIndexEntry("blade-one", "Blade One")]), ReadAt);

        var reputationStore = new ReputationStore(new StoredData(store, StoredKeys.Obsolete));
        reputationStore.Save(new StoredReputation(
            new ReputationSnapshot([new FactionStanding(1, "Good", 5, 10, 20, 0)], null), "rep.json", ReadAt));

        var source = new ReputationFileSource(_paths);
        Directory.CreateDirectory(_paths.ReputationDirectory);
        await File.WriteAllTextAsync(source.Path, "{}");
        Directory.CreateDirectory(_paths.LogDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_paths.LogDirectory, "x.txt"), [1, 2, 3]);
        Directory.CreateDirectory(_paths.ExportDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_paths.ExportDirectory, "y.zip"), [4, 5, 6]);

        var hardFarms = new HardFarmsViewModel(
            hardFarmStore, new FakeHardFarmSync(), ActiveInventoryBuilder.Empty(), new FakeUrlLauncher());
        var reputation = new ReputationViewModel(reputationStore, new FakeReputationFilePicker(), source, TimeProvider.System);

        var gameDataStore = new GameDataStore(new StoredData(store, StoredKeys.Obsolete));
        var profile = new CharacterProfile("Fictional Hero", 60, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, true, 11, 12, 13, 14, 15);
        gameDataStore.Save([new CharacterSnapshot(profile, [new FactionStanding(5, "Fictional Order", 3, 5000, 6400, 1400)], [], [], ReadAt)]);
        var sourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceToken = CancellationToken.None;
        var session = new CaptureSession(
            () => new FakePacketSource(async (_, token) =>
            {
                sourceToken = token;
                sourceStarted.SetResult();
                try
                {
                    await new TaskCompletionSource().Task.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                }
            }),
            new FakeProcessElevation(), () => NpcapStatus.Available, () => true, gameDataStore,
            new FakeDelay(), TimeProvider.System, new FakeApplicationExit(), _paths);
        await session.RestoreAsync();
        _ = session.StartAsync();
        await sourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(CaptureState.Listening, session.State);

        var stateWhenRemoved = CaptureState.Idle;
        var coordinator = new ForgetCoordinator(
            () =>
            {
                stateWhenRemoved = session.State;
                return !gateFails && storedData.Clear();
            },
            () => [hardFarms, reputation, session]);

        await hardFarms.RestoreAsync();
        await reputation.RestoreAsync();
        Assert.Single(hardFarms.Rows);
        Assert.Single(reputation.Items);
        return new World(hardFarms, reputation, coordinator, session, () => stateWhenRemoved, () => sourceToken);
    }

    private List<string> FilesUnderRoot() =>
        Directory.EnumerateFiles(_paths.Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_paths.Root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    private Dictionary<string, byte[]> SnapshotFiles() =>
        FilesUnderRoot().ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(_paths.Root, f)));

    private sealed record World(
        HardFarmsViewModel HardFarms,
        ReputationViewModel Reputation,
        ForgetCoordinator Coordinator,
        CaptureSession Session,
        Func<CaptureState> StateAtRemoval,
        Func<CancellationToken> Token)
    {
        public CaptureState StateWhenStoredDataWasRemoved => StateAtRemoval();

        public CancellationToken SourceToken => Token();
    }
}
