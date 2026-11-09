using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqwCompanion.Core.Feed;

/// <summary>
/// What the local feed tells other programs on this PC (contract 1): the logged-in characters and, for the selected one, its factions
/// and Hard Farms progress. Only what the dashboard already shows: no ids, e-mail, IP or other players. Fields are only ever added
/// within a contract number; a breaking change raises it.
/// </summary>
public sealed record FeedSnapshot(
    int Contract,
    FeedCapture Capture,
    string? Selected,
    IReadOnlyList<FeedCharacter> Characters,
    IReadOnlyList<FeedFaction> Factions,
    FeedHardFarms? HardFarms)
{
    public const int ContractNumber = 1;

    /// <summary>Nothing captured yet and the capture idle.</summary>
    public static FeedSnapshot Empty { get; } = new(ContractNumber, new FeedCapture("Not capturing.", false), null, [], [], null);
}

/// <summary>The capture in words and whether it is listening.</summary>
public sealed record FeedCapture(string State, bool Listening);

/// <summary>One logged-in character, identified by its name. <c>Live</c> is true once its connection received live updates.</summary>
public sealed record FeedCharacter(
    string Name,
    int Level,
    bool Live,
    DateTimeOffset SavedAtUtc,
    DateTimeOffset? LastLiveUpdateUtc,
    int LiveUpdates,
    FeedFigures Figures,
    FeedKill? LastKill);

public sealed record FeedFigures(long Gold, long Exp, long ExpToLevel, int Ac, int ClassPoints, int DaysPlayed);

/// <summary>The gold and experience of the last kill seen (the session keeps one, so only one character carries it).</summary>
public sealed record FeedKill(long Gold, long Exp);

public sealed record FeedFaction(string Name, int Rank, int Rep, long ToNext, double Fraction, bool Max);

/// <summary>How many Hard Farms entries the selected character owns, and the display names of those it does not (at most <see cref="MaxMissing"/>).</summary>
public sealed record FeedHardFarms(int Owned, int Total, IReadOnlyList<string> Missing)
{
    public const int MaxMissing = 200;

    public static IReadOnlyList<string> Cap(IEnumerable<string> names) => [.. names.Take(MaxMissing)];
}

public static class FeedJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(FeedSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
