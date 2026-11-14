namespace AqwCompanion.Phone.Skua;

public interface ISkuaFeed
{
    SkuaHubView Current { get; }

    event EventHandler? Changed;
}
