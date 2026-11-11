using System.Reflection;
using System.Security.Cryptography;
using AqwCompanion.Phone.Web;

namespace AqwCompanion.App.Phone;

public sealed class EmbeddedAssetSource : IAssetSource
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".webmanifest"] = "application/manifest+json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
    };

    private readonly Dictionary<string, Asset> _items = new(StringComparer.Ordinal);

    public EmbeddedAssetSource() : this(typeof(EmbeddedAssetSource).Assembly)
    {
    }

    public EmbeddedAssetSource(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("web/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var key = "/" + name["web/".Length..].Replace('\\', '/');
            var type = Types.GetValueOrDefault(Path.GetExtension(key), "application/octet-stream");
            _items[key] = new Asset(bytes, type, "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16] + "\"");
        }
    }

    public int Count => _items.Count;

    public IEnumerable<string> Paths => _items.Keys;

    public Asset? Find(string path) => _items.GetValueOrDefault(path);
}
