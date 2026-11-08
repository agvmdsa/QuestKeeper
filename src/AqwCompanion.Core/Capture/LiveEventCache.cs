using System.Diagnostics.CodeAnalysis;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// The item definitions the game announced with <c>dropItem</c> on ONE connection, kept in memory only so the matching
/// <c>getDrop</c> can build the item. Holds at most <see cref="MaxDefinitions"/> of them; the newest wins and the oldest goes
/// first. Not thread safe: the engine uses it under its own lock.
/// </summary>
public sealed class LiveEventCache
{
    /// <summary>The most definitions kept per connection.</summary>
    public const int MaxDefinitions = 64;

    private readonly Dictionary<int, OwnedItem> _definitions = [];
    private readonly List<int> _order = [];

    /// <summary>How many definitions are kept.</summary>
    public int Count => _definitions.Count;

    /// <summary>Keeps the definition under its <see cref="OwnedItem.ItemId"/>, replacing an older one and dropping the oldest when full.</summary>
    public void Store(OwnedItem definition)
    {
        if (_definitions.ContainsKey(definition.ItemId))
        {
            _order.Remove(definition.ItemId);
        }
        else if (_definitions.Count >= MaxDefinitions)
        {
            _definitions.Remove(_order[0]);
            _order.RemoveAt(0);
        }

        _definitions[definition.ItemId] = definition;
        _order.Add(definition.ItemId);
    }

    /// <summary>The definition kept for this item id, if any.</summary>
    public bool TryGet(int itemId, [MaybeNullWhen(false)] out OwnedItem definition) =>
        _definitions.TryGetValue(itemId, out definition);
}
