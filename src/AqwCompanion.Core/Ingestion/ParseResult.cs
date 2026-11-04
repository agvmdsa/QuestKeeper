namespace AqwCompanion.Core.Ingestion;

public sealed record ParseResult<T>
{
    public required ParseOutcome Outcome { get; init; }
    public required ParseDiagnostics Diagnostics { get; init; }
    public T? Data { get; init; }
    public string? Reason { get; init; }

    public static ParseResult<T> Ok(T data, ParseDiagnostics diagnostics) =>
        new() { Outcome = ParseOutcome.Ok, Data = data, Diagnostics = diagnostics };

    public static ParseResult<T> Suspicious(T? data, string reason, ParseDiagnostics diagnostics) =>
        new() { Outcome = ParseOutcome.Suspicious, Data = data, Reason = reason, Diagnostics = diagnostics };

    public static ParseResult<T> Failed(string reason, ParseDiagnostics diagnostics) =>
        new() { Outcome = ParseOutcome.Failed, Reason = reason, Diagnostics = diagnostics };
}
