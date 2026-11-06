using AqwCompanion.Core.Badges;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Site;
using Serilog;

namespace AqwCompanion.App.Badges;

/// <summary>One GET of the public badges list, then the normal ingestion pipeline (wide event, failed-snapshot buffer).</summary>
public sealed class CharacterBadgesLoader(
    IPageFetcher fetcher,
    IngestionService<CharacterBadges> ingestion) : ICharacterBadgesLoader
{
    public async Task<BadgesLoadResult> LoadAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            var fetched = await fetcher.FetchAsync(AqwUrls.Badges(characterId), ct).ConfigureAwait(false);
            if (fetched.Snapshot is not { } snapshot)
            {
                return new BadgesLoadResult(BadgesLoadStatus.Unavailable);
            }

            var result = await ingestion.IngestAsync(snapshot, ct).ConfigureAwait(false);
            return result.Parse is { Outcome: not ParseOutcome.Failed, Data: { } badges }
                ? new BadgesLoadResult(BadgesLoadStatus.Loaded, badges, result.Persisted)
                : new BadgesLoadResult(BadgesLoadStatus.NotFound);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Type only: the message could carry the URL.
            Log.Error("Loading the character badges failed: {ExceptionType}", ex.GetType().Name);
            return new BadgesLoadResult(BadgesLoadStatus.Unavailable);
        }
    }
}
