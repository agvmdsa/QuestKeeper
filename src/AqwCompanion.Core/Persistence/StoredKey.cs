namespace AqwCompanion.Core.Persistence;

/// <summary>A record declared once: its name, the version of the stored shape, and the feature that owns it.</summary>
public sealed record StoredKey<T>(string Name, int Version, string Owner) : IStoredKey;
