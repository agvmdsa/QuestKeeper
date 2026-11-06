using AqwCompanion.Core.Equipment;

namespace AqwCompanion.App.Equipment;

public enum EquipmentLoadStatus
{
    /// <summary>The page was read and equipment was found.</summary>
    Loaded,

    /// <summary>The page could not be fetched (network, HTTP error, timeout...).</summary>
    Unavailable,

    /// <summary>The page was fetched but no equipment could be read from it.</summary>
    NotFound,
}

/// <param name="Persisted">True only when the ingestion saved an <c>Ok</c> parse; a suspicious result is shown but never stored.</param>
public sealed record EquipmentLoadResult(EquipmentLoadStatus Status, CharacterEquipment? Equipment = null, bool Persisted = false);

/// <summary>Fetches the public character page for a name and reads the equipped items. Never throws.</summary>
public interface ICharacterEquipmentLoader
{
    Task<EquipmentLoadResult> LoadAsync(string characterName, CancellationToken ct = default);
}
