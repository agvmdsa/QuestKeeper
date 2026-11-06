using AqwCompanion.Core.Ingestion;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Site;
using Serilog;

namespace AqwCompanion.App.Inventory;

/// <summary>One GET of the public inventory list, then the normal ingestion pipeline (wide event, failed-snapshot buffer).</summary>
public sealed class CharacterInventoryLoader(
    IPageFetcher fetcher,
    IngestionService<CharacterInventory> ingestion) : ICharacterInventoryLoader
{
    public async Task<InventoryLoadResult> LoadAsync(int characterId, CancellationToken ct = default)
    {
        try
        {
            var fetched = await fetcher.FetchAsync(AqwUrls.Inventory(characterId), ct).ConfigureAwait(false);
            if (fetched.Snapshot is not { } snapshot)
            {
                return new InventoryLoadResult(InventoryLoadStatus.Unavailable);
            }

            var result = await ingestion.IngestAsync(snapshot, ct).ConfigureAwait(false);
            return result.Parse is { Outcome: not ParseOutcome.Failed, Data: { } inventory }
                ? new InventoryLoadResult(InventoryLoadStatus.Loaded, inventory, result.Persisted)
                : new InventoryLoadResult(InventoryLoadStatus.NotFound);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Type only: the message could carry the URL.
            Log.Error("Loading the character inventory failed: {ExceptionType}", ex.GetType().Name);
            return new InventoryLoadResult(InventoryLoadStatus.Unavailable);
        }
    }
}
