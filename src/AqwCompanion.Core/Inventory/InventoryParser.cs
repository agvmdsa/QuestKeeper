using System.Text.Json;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Site;

namespace AqwCompanion.Core.Inventory;

/// <summary>
/// Reads the public inventory list (a JSON array of objects with name, type, count and the AC / member flags).
/// Parsing never throws and nothing parsed is logged.
/// </summary>
public sealed class InventoryParser : IPageParser<CharacterInventory>
{
    public const string ArrayMarker = "json array of inventory items";
    public const string NameMarker = "strName";

    private const string ArrayStrategy = "json-array";
    private const int MaxNameLength = 200;
    private const int MaxTypeLength = 100;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 8 };

    public string Name => "character-inventory";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) => AqwUrls.IsInventory(snapshot.Url);

    public ParseResult<CharacterInventory> Parse(PageSnapshot snapshot)
    {
        var body = snapshot.Payload.Trim();
        if (body.Length == 0 || body.EqualsIgnoreCase("error"))
        {
            return Failed("the inventory endpoint reported an error", [ArrayMarker]);
        }

        try
        {
            using var document = JsonDocument.Parse(body, Options);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Failed("the inventory response is not a list", [ArrayMarker]);
            }

            var items = new List<InventoryItem>();
            var entries = 0;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                entries++;
                if (ReadItem(entry) is { } item)
                {
                    items.Add(item);
                }
            }

            var diagnostics = new ParseDiagnostics(Name, Version, ArrayStrategy, [], items.Count);
            return entries > 0 && items.Count == 0
                ? ParseResult<CharacterInventory>.Suspicious(
                    new CharacterInventory(items), "no inventory entry had a name", diagnostics with { MissingMarkers = [NameMarker] })
                : ParseResult<CharacterInventory>.Ok(new CharacterInventory(items), diagnostics);
        }
        catch (JsonException)
        {
            return Failed("the inventory response is not valid JSON", [ArrayMarker]);
        }
    }

    private ParseResult<CharacterInventory> Failed(string reason, IReadOnlyList<string> missing) =>
        ParseResult<CharacterInventory>.Failed(reason, new ParseDiagnostics(Name, Version, null, missing, 0));

    private static InventoryItem? ReadItem(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = entry.ReadCleanText("strName", MaxNameLength);
        if (name is null)
        {
            return null;
        }

        var type = entry.ReadCleanText("strType", MaxTypeLength) ?? CharacterInventory.OtherTypeName;
        var isClass = type.EqualsIgnoreCase(CharacterInventory.ClassTypeName);
        var count = entry.ReadNonNegativeInt32("intCount") ?? (isClass ? 0 : 1);
        return new InventoryItem(name, type, count, ReadFlag(entry, "bCoins"), ReadFlag(entry, "bUpgrade"));
    }

    private static bool ReadFlag(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
