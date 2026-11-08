using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.Inventory;

/// <summary>The inventory the rest of the app reads, so a feature can be tested without a running capture.</summary>
public interface IActiveInventory
{
    /// <summary>Raised on the UI thread when the inventory may have changed.</summary>
    event EventHandler? Changed;

    /// <summary>The selected Character's captured inventory, or null while there is none.</summary>
    CharacterInventory? Current { get; }
}
