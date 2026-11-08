using AqwCompanion.App.GameData;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Inventory;

public class ActiveInventoryTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly CaptureRig _rig = new();
    private readonly GameDataViewModel _gameData;
    private readonly ActiveInventory _active;
    private int _changes;

    public ActiveInventoryTests()
    {
        _gameData = _rig.CreateViewModel();
        _active = new ActiveInventory(_gameData);
        _active.Changed += (_, _) => _changes++;
    }

    private async Task Capture(params string[] names)
    {
        _rig.Source = CaptureRig.Feeding(names.SelectMany((name, i) => Connection(clientPort: 50000 + i, name: name).Server));
        await _gameData.StartCommand.ExecuteAsync(null).WaitAsync(Wait);
    }

    [Fact]
    public void Without_a_snapshot_there_is_no_inventory()
    {
        Assert.Null(_active.Current);
    }

    [Fact]
    public async Task With_a_snapshot_the_converted_inventory_is_used()
    {
        await Capture("Fictional Hero");

        Assert.NotNull(_active.Current);
        Assert.True(_active.Current.Has("Fictional Blade"));
        Assert.True(_active.Current.Has("Fictional Chair"));
        Assert.Same(_active.Current, _active.Current);
    }

    [Fact]
    public async Task A_capture_raises_Changed_and_forgetting_it_leaves_no_inventory()
    {
        var before = _changes;

        await Capture("Fictional Hero");

        Assert.True(_changes > before);
        before = _changes;
        ((IForgettable)_rig.Session).Forget();

        Assert.True(_changes > before);
        Assert.Null(_active.Current);
    }

    [Fact]
    public async Task Selecting_another_character_changes_the_inventory_and_raises_Changed()
    {
        await Capture("First Hero", "Second Hero");
        var first = _gameData.SelectedCharacter;
        var inventory = _active.Current;
        var before = _changes;

        _gameData.SelectedCharacter = first == "First Hero" ? "Second Hero" : "First Hero";

        Assert.Equal(before + 1, _changes);
        Assert.NotSame(inventory, _active.Current);
    }
}
