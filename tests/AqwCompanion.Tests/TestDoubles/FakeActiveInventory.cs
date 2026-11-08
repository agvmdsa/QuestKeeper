using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>An inventory the test sets by hand; changing it raises <see cref="Changed"/> like the real one.</summary>
public sealed class FakeActiveInventory : IActiveInventory
{
    private CharacterInventory? _data;

    public event EventHandler? Changed;

    public CharacterInventory? Current => _data;

    public CharacterInventory? Data
    {
        get => _data;
        set
        {
            _data = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
