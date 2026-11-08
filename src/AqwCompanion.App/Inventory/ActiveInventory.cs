using AqwCompanion.App.GameData;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.Inventory;

/// <summary>
/// The inventory the rest of the app reads: the selected Character's captured inventory, or null while there is no
/// Capture data. Raises <see cref="Changed"/> on the UI thread.
/// </summary>
public sealed class ActiveInventory : IActiveInventory
{
    private readonly GameDataViewModel _gameData;

    // The conversion of the last snapshot seen, so Current stays the same object while the snapshot does.
    private CharacterSnapshot? _convertedFrom;
    private CharacterInventory? _converted;

    public ActiveInventory(GameDataViewModel gameData)
    {
        _gameData = gameData;
        gameData.ActiveSnapshotChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the selected Character's snapshot changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The captured inventory of the selected Character, or null when it has no snapshot.</summary>
    public CharacterInventory? Current
    {
        get
        {
            var snapshot = _gameData.ActiveSnapshot;
            if (snapshot is null)
            {
                return null;
            }

            if (!ReferenceEquals(snapshot, _convertedFrom))
            {
                _converted = SnapshotInventory.ToCharacterInventory(snapshot);
                _convertedFrom = snapshot;
            }

            return _converted;
        }
    }
}
