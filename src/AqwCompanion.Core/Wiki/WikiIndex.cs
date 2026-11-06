namespace AqwCompanion.Core.Wiki;

public sealed record WikiIndexEntry(string Slug, string Name);

/// <summary>One parsed type index page: which index it is, its type and its (slug, name) entries, distinct by slug.</summary>
public sealed record WikiIndex(string IndexSlug, WikiType Type, IReadOnlyList<WikiIndexEntry> Entries);
