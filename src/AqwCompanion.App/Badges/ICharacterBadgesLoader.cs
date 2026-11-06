using AqwCompanion.Core.Aqw;

namespace AqwCompanion.App.Badges;

public enum BadgesLoadStatus
{
    /// <summary>The list was read (it may be empty).</summary>
    Loaded,

    /// <summary>The list could not be fetched (network, HTTP error, timeout...).</summary>
    Unavailable,

    /// <summary>The list was fetched but could not be read.</summary>
    NotFound,
}

/// <param name="Persisted">True only when the ingestion saved an <c>Ok</c> parse; a suspicious result is shown but never stored.</param>
public sealed record BadgesLoadResult(BadgesLoadStatus Status, CharacterBadges? Badges = null, bool Persisted = false);

/// <summary>Fetches the public badges list for a character id. Never throws.</summary>
public interface ICharacterBadgesLoader
{
    Task<BadgesLoadResult> LoadAsync(int characterId, CancellationToken ct = default);
}
