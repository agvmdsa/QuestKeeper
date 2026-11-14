namespace AqwCompanion.Skua;

/// <summary>How <see cref="SkuaScriptCatalog"/> reads the facts of the scripts: the time one scan may spend, and a look into the cache.</summary>
public sealed class SkuaScriptFactsOptions
{
    /// <summary>Well under the 15 s the phone waits for a list.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(8);

    /// <summary>The full path in, the facts when they are cached and still current, else null. Must not read the file's text.</summary>
    public Func<string, SkuaScriptFacts?>? Cached { get; init; }

    /// <summary>Called after a scan with every path it considered, so the cache can drop the rest.</summary>
    public Action<IReadOnlySet<string>>? Retain { get; init; }

    /// <summary>How long one scan may read files before listing the rest without header data.</summary>
    public TimeSpan Budget { get; init; } = DefaultBudget;

    /// <summary>A clock in milliseconds; null means the system's. Tests pass a fake one.</summary>
    public Func<long>? ClockMs { get; init; }
}
