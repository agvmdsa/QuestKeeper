using System.Collections;
using System.Reflection;
using System.Text.Json;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Capture;

/// <summary>Asserts that no marker secret appears anywhere in an object graph or its serialized forms.</summary>
internal static class SecretScan
{
    public static void AssertNoSecrets(object? value)
    {
        var strings = new List<string>();
        Collect(value, strings, 0);
        strings.Add(JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object)));

        foreach (var text in strings)
        {
            foreach (var secret in Secrets)
            {
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static void Collect(object? value, List<string> strings, int depth)
    {
        if (value is null || depth > 12)
        {
            return;
        }

        if (value is string text)
        {
            strings.Add(text);
            return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is DateTimeOffset or DateTime or Guid or decimal)
        {
            strings.Add(value.ToString() ?? string.Empty);
            return;
        }

        strings.Add(value.ToString() ?? string.Empty);
        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                Collect(item, strings, depth + 1);
            }
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                strings.Add(property.Name);
                Collect(property.GetValue(value), strings, depth + 1);
            }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Collect(field.GetValue(value), strings, depth + 1);
        }
    }
}
