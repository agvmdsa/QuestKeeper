namespace AqwCompanion.Core.Ingestion;

public sealed record ParseDiagnostics(
    string ParserName,
    string ParserVersion,
    string? MatchedStrategy,
    IReadOnlyList<string> MissingMarkers,
    int ItemCount)
{
    public static ParseDiagnostics Empty(string parserName, string parserVersion) =>
        new(parserName, parserVersion, null, [], 0);
}
