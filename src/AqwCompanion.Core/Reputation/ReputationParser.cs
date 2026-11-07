using System.Text.Json;
using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Reputation;

/// <summary>
/// Reads a reputation file (schema 1: an object with a list of faction standings). The size is checked before the content is
/// read, an entry that fails validation is skipped and counted, and nothing is accepted from a file that is not valid as a whole.
/// Parsing never throws and nothing parsed is logged.
/// </summary>
public static class ReputationParser
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxFactions = 500;
    public const string TooLargeError = "The file is too large (the limit is 1 MiB).";
    public const string NotJsonError = "The file is not valid JSON.";
    public const string NotObjectError = "The file does not contain a JSON object.";
    public const string UnsupportedSchemaError = "Unsupported schema.";
    public const string NoFactionsError = "The file has no list of factions.";
    public const string TooManyFactionsError = "The file has more than 500 factions.";
    public const string NoValidEntriesError = "The file has no valid faction entries.";

    private const int SupportedSchema = 1;
    private const int MaxNameLength = 200;
    private const int MaxExportedAtLength = 40;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 8 };

    /// <summary>Reads the stream, never more than <see cref="MaxBytes"/> plus one byte, and parses it; a stream that reports a larger length is not read at all.</summary>
    public static async Task<ReputationParseResult> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek && stream.Length > MaxBytes)
        {
            return ReputationParseResult.Failure(TooLargeError);
        }

        var buffer = new byte[MaxBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Parse(buffer.AsMemory(0, total));
    }

    public static ReputationParseResult Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > MaxBytes)
        {
            return ReputationParseResult.Failure(TooLargeError);
        }

        try
        {
            using var document = JsonDocument.Parse(utf8, Options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ReputationParseResult.Failure(NotObjectError);
            }

            if (!root.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out var version)
                || version != SupportedSchema)
            {
                return ReputationParseResult.Failure(UnsupportedSchemaError);
            }

            if (!root.TryGetProperty("factions", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return ReputationParseResult.Failure(NoFactionsError);
            }

            if (list.GetArrayLength() > MaxFactions)
            {
                return ReputationParseResult.Failure(TooManyFactionsError);
            }

            var factions = new List<FactionStanding>();
            var seen = new HashSet<int>();
            var skipped = 0;
            foreach (var entry in list.EnumerateArray())
            {
                if (ReadStanding(entry) is { } standing && seen.Add(standing.FactionId))
                {
                    factions.Add(standing);
                }
                else
                {
                    skipped++;
                }
            }

            return factions.Count == 0
                ? ReputationParseResult.Failure(NoValidEntriesError)
                : ReputationParseResult.Success(new ReputationSnapshot(factions, root.ReadCleanText("exportedAt", MaxExportedAtLength)), skipped);
        }
        catch (JsonException)
        {
            return ReputationParseResult.Failure(NotJsonError);
        }
    }

    private static FactionStanding? ReadStanding(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = entry.ReadCleanText("name", MaxNameLength);
        var id = entry.ReadNonNegativeInt32("factionId");
        var rank = entry.ReadNonNegativeInt32("rank");
        var rep = entry.ReadNonNegativeInt32("rep");
        var toRank = entry.ReadNonNegativeInt32("repToRank");
        var spill = entry.TryGetProperty("spillRep", out _) ? entry.ReadNonNegativeInt32("spillRep") : 0;
        return name is null || id is null || rank is null || rep is null || toRank is null || spill is null
            ? null
            : new FactionStanding(id.Value, name, rank.Value, rep.Value, toRank.Value, spill.Value);
    }
}
