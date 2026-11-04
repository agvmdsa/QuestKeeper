using System.Text.RegularExpressions;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Test-only parser that demonstrates the fixture-driven parsing workflow.</summary>
public sealed partial class TableRowsTestParser : IPageParser<IReadOnlyList<string>>
{
    public string Name => "test-table-rows";
    public string Version => "1";

    public bool CanParse(PageSnapshot snapshot) =>
        snapshot.Url.EndsWith("/Inventory", StringComparison.OrdinalIgnoreCase);

    public ParseResult<IReadOnlyList<string>> Parse(PageSnapshot snapshot)
    {
        if (!snapshot.Payload.Contains("id=\"inventoryTable\"", StringComparison.Ordinal))
        {
            return ParseResult<IReadOnlyList<string>>.Failed(
                "inventory table marker missing",
                new ParseDiagnostics(Name, Version, null, ["#inventoryTable"], 0));
        }

        IReadOnlyList<string> names = NameCell().Matches(snapshot.Payload).Select(m => m.Groups[1].Value).ToList();
        var diagnostics = new ParseDiagnostics(Name, Version, "table-rows", [], names.Count);

        return names.Count == 0
            ? ParseResult<IReadOnlyList<string>>.Suspicious(names, "table present but no rows", diagnostics)
            : ParseResult<IReadOnlyList<string>>.Ok(names, diagnostics);
    }

    [GeneratedRegex("""<td class="name">(.*?)</td>""")]
    private static partial Regex NameCell();
}
