using System.Text;
using AqwCompanion.Phone.Web;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeAssets : IAssetSource
{
    public Dictionary<string, Asset> Items { get; } = new()
    {
        ["/index.html"] = new(Encoding.UTF8.GetBytes("<!doctype html><title>Questkeeper</title>"), "text/html; charset=utf-8", "\"abc\""),
        ["/js/main.js"] = new(Encoding.UTF8.GetBytes("export {};"), "text/javascript; charset=utf-8", "\"def\""),
    };

    public Asset? Find(string path) => Items.GetValueOrDefault(path);
}
