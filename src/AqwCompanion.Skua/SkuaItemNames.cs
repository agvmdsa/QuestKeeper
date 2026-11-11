namespace AqwCompanion.Skua;

/// <summary>Names and quantities of the inventory as of the last snapshot, so a sale (which arrives after the item left) can still be named. Thread-safe.</summary>
public sealed class SkuaItemNames
{
    private readonly object _lock = new();
    private Dictionary<int, (string Name, int Quantity)> _items = [];

    public void Replace(IEnumerable<(int Id, string Name, int Quantity)> items)
    {
        var next = new Dictionary<int, (string, int)>();
        foreach (var (id, name, quantity) in items)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                next[id] = (name, quantity);
            }
        }

        lock (_lock)
        {
            _items = next;
        }
    }

    public string? NameOf(int id)
    {
        lock (_lock)
        {
            return _items.TryGetValue(id, out var item) ? item.Name : null;
        }
    }

    public int QuantityOf(int id)
    {
        lock (_lock)
        {
            return _items.TryGetValue(id, out var item) ? item.Quantity : 0;
        }
    }
}
