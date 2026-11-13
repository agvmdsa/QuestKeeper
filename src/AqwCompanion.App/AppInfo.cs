using System.Reflection;

namespace AqwCompanion.App;

public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string ProductName { get; } =
        Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "SkuaLink";

    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static string ExeName { get; } = Assembly.GetName().Name ?? "AqwCompanion";
}
