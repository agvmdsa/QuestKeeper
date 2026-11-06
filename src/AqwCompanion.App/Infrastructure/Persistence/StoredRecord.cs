using LiteDB;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>One stored value: the key, the format version, when it was read, and its JSON text.</summary>
public sealed class StoredRecord
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public long ReadAtUnixMs { get; set; }

    public string Payload { get; set; } = string.Empty;
}
