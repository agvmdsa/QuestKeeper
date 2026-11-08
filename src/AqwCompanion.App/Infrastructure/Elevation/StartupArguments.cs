namespace AqwCompanion.App.Infrastructure.Elevation;

/// <summary>The command line the app understands: the data root an elevated relaunch must keep using, and the background start.</summary>
public static class StartupArguments
{
    public const string DataRootOption = "--data-root=";

    /// <summary>Starts the app in the notification area without opening its window, as the sign-in task does.</summary>
    public const string BackgroundOption = "--background";

    public static bool IsBackground(string[] args) => args.Contains(BackgroundOption, StringComparer.Ordinal);

    /// <summary>The value after the option, without surrounding quotes; null when absent or empty.</summary>
    public static string? TryGetDataRoot(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith(DataRootOption, StringComparison.Ordinal))
            {
                continue;
            }

            var value = arg[DataRootOption.Length..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }
            value = value.Trim('"');
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    /// <summary>The quoted option for a relaunch, with embedded quotes escaped.</summary>
    public static string ForRelaunch(string dataRoot) =>
        DataRootOption + "\"" + dataRoot.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
