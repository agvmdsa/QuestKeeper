using AqwCompanion.App.Infrastructure.Browser;
using AqwCompanion.Core.Account;
using AqwCompanion.Core.Ingestion;
using Serilog;

namespace AqwCompanion.App.SignIn;

internal enum AccountHomeReadFailure
{
    None,

    /// <summary>The flow ended while reading; nothing is reported.</summary>
    Dismissed,

    /// <summary>The page was captured but no character name could be read from it.</summary>
    NotRecognized,

    TimedOut,

    CaptureFailed,
}

internal readonly record struct AccountHomeRead(
    string? Name,
    AccountSummary? Summary,
    AccountHomeReadFailure Failure,
    string? CaptureError = null);

/// <summary>One read of the account home page: capture, ingest, classify. It knows no UI messages.</summary>
internal sealed class AccountHomeReader
{
    private readonly IBrowserHost _host;
    private readonly IngestionService<AccountIdentity> _ingestion;
    private readonly TimeSpan _readTimeout;
    private readonly ILogger _log;

    public AccountHomeReader(
        IBrowserHost host, IngestionService<AccountIdentity> ingestion, TimeSpan readTimeout, ILogger log)
    {
        _host = host;
        _ingestion = ingestion;
        _readTimeout = readTimeout;
        _log = log;
    }

    /// <param name="lifetime">Cancelled when the flow ends.</param>
    /// <param name="isStopped">True once the flow is finished or disposed.</param>
    public async Task<AccountHomeRead> ReadAsync(CancellationToken lifetime, Func<bool> isStopped)
    {
        if (isStopped())
        {
            return Failed(AccountHomeReadFailure.Dismissed);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            timeout.CancelAfter(_readTimeout);
            var snapshot = await _host.CaptureAsync(timeout.Token).WaitAsync(timeout.Token);
            if (isStopped())
            {
                // Dismissed while capturing: nothing may be stored or buffered.
                return Failed(AccountHomeReadFailure.Dismissed);
            }

            var result = await _ingestion.IngestAsync(snapshot, timeout.Token);
            if (isStopped())
            {
                return Failed(AccountHomeReadFailure.Dismissed);
            }

            return result.Parse.Outcome == ParseOutcome.Ok && result.Parse.Data is { } identity
                ? new AccountHomeRead(identity.CharacterName, identity.Summary, AccountHomeReadFailure.None)
                : Failed(AccountHomeReadFailure.NotRecognized);
        }
        catch (OperationCanceledException) when (isStopped() || lifetime.IsCancellationRequested)
        {
            // Dismissed (or disposed) while reading: not a timeout, so nothing is reported.
            return Failed(AccountHomeReadFailure.Dismissed);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Reading the account home page timed out after {Timeout}", _readTimeout);
            return Failed(AccountHomeReadFailure.TimedOut);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(ex, "Capturing the account home page failed");
            return new AccountHomeRead(null, null, AccountHomeReadFailure.CaptureFailed, ex.GetType().Name);
        }
    }

    private static AccountHomeRead Failed(AccountHomeReadFailure failure) => new(null, null, failure);
}
