namespace AqwCompanion.App;

public sealed record AppPaths(string Root)
{
    public string LogDirectory => Path.Combine(Root, "logs");
    public string CrashDirectory => Path.Combine(Root, "crashes");
    public string ExportDirectory => Path.Combine(Root, "exports");
    public string DataDirectory => Path.Combine(Root, "data");
    public string ReputationDirectory => Path.Combine(Root, "reputation");
    public string FeedTokenPath => Path.Combine(Root, "feed.token");
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string PhonePath => Path.Combine(Root, "phone.json");

    /// <summary>An empty flag file kept while the phone page is published through Tailscale; not character data.</summary>
    public string PhoneServeMarkerPath => Path.Combine(Root, "phone-serve.flag");
    public string DatabasePath => Path.Combine(DataDirectory, "companion.db");

    /// <summary>The per-user root, or <paramref name="dataRoot"/> when it names an existing directory (an elevated relaunch).</summary>
    public static AppPaths ForCurrentUser(string appName, string? dataRoot = null) =>
        dataRoot is not null && Directory.Exists(dataRoot)
            ? new(dataRoot)
            : new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName));
}
