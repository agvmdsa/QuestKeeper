using AqwCompanion.Phone.Web;

namespace AqwCompanion.App.Phone;

/// <summary>An asset source that finds nothing, so the host answers 404 until the page is embedded.</summary>
public sealed class EmptyAssetSource : IAssetSource
{
    public Asset? Find(string path) => null;
}
