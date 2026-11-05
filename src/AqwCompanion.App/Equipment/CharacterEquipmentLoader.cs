using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using Serilog;

namespace AqwCompanion.App.Equipment;

/// <summary>One GET of the public character page, then the normal ingestion pipeline (wide event, failed-snapshot buffer).</summary>
public sealed class CharacterEquipmentLoader(
    IPageFetcher fetcher,
    IngestionService<CharacterEquipment> ingestion) : ICharacterEquipmentLoader
{
    public async Task<EquipmentLoadResult> LoadAsync(string characterName, CancellationToken ct = default)
    {
        try
        {
            var fetched = await fetcher.FetchAsync(AqwUrls.CharPage(characterName), ct).ConfigureAwait(false);
            if (fetched.Snapshot is not { } snapshot)
            {
                return new EquipmentLoadResult(EquipmentLoadStatus.Unavailable);
            }

            var result = await ingestion.IngestAsync(snapshot, ct).ConfigureAwait(false);
            return result.Parse is { Outcome: not ParseOutcome.Failed, Data: { } equipment }
                ? new EquipmentLoadResult(EquipmentLoadStatus.Loaded, equipment)
                : new EquipmentLoadResult(EquipmentLoadStatus.NotFound);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Type only: the message could carry the character name or URL.
            Log.Error("Loading the character equipment failed: {ExceptionType}", ex.GetType().Name);
            return new EquipmentLoadResult(EquipmentLoadStatus.Unavailable);
        }
    }
}
