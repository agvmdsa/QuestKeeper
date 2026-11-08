using System.ComponentModel;
using AqwCompanion.App.GameData;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.Inventory;

/// <summary>
/// The inventory the rest of the app reads: the selected Character's captured inventory when the Capture has one (the
/// Primary source), otherwise the public inventory from the website. Raises <see cref="Changed"/> on the UI thread.
/// </summary>
public sealed class ActiveInventory
{
    private readonly InventoryCardViewModel _site;
    private readonly GameDataViewModel _gameData;

    // The conversion of the last snapshot seen, so Current stays the same object while the snapshot does.
    private CharacterSnapshot? _convertedFrom;
    private CharacterInventory? _converted;

    public ActiveInventory(InventoryCardViewModel site, GameDataViewModel gameData)
    {
        _site = site;
        _gameData = gameData;
        site.PropertyChanged += OnSiteChanged;
        gameData.ActiveSnapshotChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when either source changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The captured inventory of the selected Character when it has a snapshot, else the site's inventory (or null).</summary>
    public CharacterInventory? Current
    {
        get
        {
            var snapshot = _gameData.ActiveSnapshot;
            if (snapshot is null)
            {
                return _site.Data;
            }

            if (!ReferenceEquals(snapshot, _convertedFrom))
            {
                _converted = SnapshotInventory.ToCharacterInventory(snapshot);
                _convertedFrom = snapshot;
            }

            return _converted;
        }
    }

    public bool IsFromCapture => _gameData.ActiveSnapshot is not null;

    private void OnSiteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InventoryCardViewModel.Data))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
