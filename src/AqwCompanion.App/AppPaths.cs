namespace AqwCompanion.App;

public sealed record AppPaths(string Root)
{
    public string LogDirectory => Path.Combine(Root, "logs");
    public string CrashDirectory => Path.Combine(Root, "crashes");
    public string ExportDirectory => Path.Combine(Root, "exports");
    public string DataDirectory => Path.Combine(Root, "data");
    public string ReputationDirectory => Path.Combine(Root, "reputation");
    public string DatabasePath => Path.Combine(DataDirectory, "companion.db");

    public static AppPaths ForCurrentUser(string appName) =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName));
}
