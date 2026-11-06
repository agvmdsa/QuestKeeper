using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>
/// The one set of JSON options for stored payloads: camelCase, and only settable (constructor/init) properties, so
/// computed members such as groupings and previews are never written.
/// </summary>
public static class StoredJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            for (var i = info.Properties.Count - 1; i >= 0; i--)
            {
                if (info.Properties[i].Set is null)
                {
                    info.Properties.RemoveAt(i);
                }
            }
        });

        return new JsonSerializerOptions
        {
            TypeInfoResolver = resolver,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }
}
