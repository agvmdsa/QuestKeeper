using AqwCompanion.Core.Aqw;

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

public sealed record EquipmentLoadResult(EquipmentLoadStatus Status, CharacterEquipment? Equipment = null);

/// <summary>Fetches the public character page for a name and reads the equipped items. Never throws.</summary>
public interface ICharacterEquipmentLoader
{
    Task<EquipmentLoadResult> LoadAsync(string characterName, CancellationToken ct = default);
}
