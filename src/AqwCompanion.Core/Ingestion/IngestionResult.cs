namespace AqwCompanion.Core.Ingestion;

public sealed record IngestionResult<T>(ParseResult<T> Parse, bool Persisted);
