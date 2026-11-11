namespace AqwCompanion.Phone.Skua;

public interface ISkuaFeed
{
    SkuaView Current { get; }

    event EventHandler? Changed;
}
