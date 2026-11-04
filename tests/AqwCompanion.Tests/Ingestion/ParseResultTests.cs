using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Ingestion;

public class ParseResultTests
{
    private static readonly ParseDiagnostics Diag = ParseDiagnostics.Empty("p", "1");

    [Fact]
    public void Ok_carries_data_and_outcome()
    {
        var result = ParseResult<string>.Ok("x", Diag);

        Assert.Equal(ParseOutcome.Ok, result.Outcome);
        Assert.Equal("x", result.Data);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Suspicious_carries_reason_and_optional_data()
    {
        var result = ParseResult<string>.Suspicious(null, "no rows", Diag);

        Assert.Equal(ParseOutcome.Suspicious, result.Outcome);
        Assert.Equal("no rows", result.Reason);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Failed_has_no_data()
    {
        var result = ParseResult<string>.Failed("marker missing", Diag);

        Assert.Equal(ParseOutcome.Failed, result.Outcome);
        Assert.Null(result.Data);
        Assert.Equal("marker missing", result.Reason);
    }

    [Fact]
    public void Empty_diagnostics_names_the_parser_and_has_no_markers()
    {
        Assert.Equal("p", Diag.ParserName);
        Assert.Equal("1", Diag.ParserVersion);
        Assert.Empty(Diag.MissingMarkers);
        Assert.Equal(0, Diag.ItemCount);
        Assert.Null(Diag.MatchedStrategy);
    }
}
