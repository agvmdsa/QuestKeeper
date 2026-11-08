using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AqwCompanion.Core.Ingestion;

public sealed class IngestionService<T>(
    IPageParser<T> parser,
    IResultStore<T> store,
    FailedSnapshotBuffer failures,
    ILogger<IngestionService<T>> logger)
{
    public const double DropThreshold = 0.5;
    public const int DropMinPrevious = 10;

    public async Task<IngestionResult<T>> IngestAsync(PageSnapshot snapshot, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var (result, error) = SafeParse(snapshot);
        var persisted = false;

        if (result.Outcome == ParseOutcome.Ok)
        {
            try
            {
                var count = result.Diagnostics.ItemCount;
                var last = await store.GetLastCountAsync(ct);

                if (last is >= DropMinPrevious && count < last.Value * DropThreshold)
                {
                    result = ParseResult<T>.Suspicious(
                        result.Data,
                        $"item count dropped from {last.Value} to {count}",
                        result.Diagnostics);
                }
                else
                {
                    await store.SaveAsync(result.Data!, count, ct);
                    persisted = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = ParseResult<T>.Failed(
                    $"persistence failed: {ex.GetType().Name}",
                    result.Diagnostics);
                error = ex;
            }
        }

        if (result.Outcome != ParseOutcome.Ok)
        {
            failures.Record(snapshot);
        }

        LogOutcome(snapshot, result, persisted, error, Stopwatch.GetElapsedTime(started));
        return new IngestionResult<T>(result, persisted);
    }

    private (ParseResult<T> Result, Exception? Error) SafeParse(PageSnapshot snapshot)
    {
        try
        {
            return (parser.Parse(snapshot), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = ParseResult<T>.Failed(
                $"parser threw {ex.GetType().Name}",
                ParseDiagnostics.Empty(parser.Name, parser.Version));
            return (failed, ex);
        }
    }

    private void LogOutcome(
        PageSnapshot snapshot,
        ParseResult<T> result,
        bool persisted,
        Exception? error,
        TimeSpan duration)
    {
        var level = result.Outcome switch
        {
            ParseOutcome.Ok => LogLevel.Information,
            ParseOutcome.Suspicious => LogLevel.Warning,
            _ => LogLevel.Error,
        };

        logger.Log(
            level,
            error,
            "Ingestion {Outcome} page={PageUrl} parser={SafeParser} parserVersion={SafeParserVersion} strategy={SafeStrategy} items={ItemCount} persisted={Persisted} durationMs={DurationMs} reason={Reason}",
            result.Outcome,
            snapshot.Url,
            result.Diagnostics.ParserName,
            result.Diagnostics.ParserVersion,
            result.Diagnostics.MatchedStrategy,
            result.Diagnostics.ItemCount,
            persisted,
            duration.TotalMilliseconds,
            result.Reason);
    }
}
