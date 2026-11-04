namespace AqwCompanion.Core.Diagnostics;

public static class CrashReportWriter
{
    public static string Write(
        Exception exception,
        string directory,
        string appVersion,
        DateTimeOffset now,
        SnapshotRedactor? redactor = null)
    {
        redactor ??= new SnapshotRedactor();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"crash-{now:yyyyMMdd-HHmmssfff}.txt");
        var text = $"AppVersion: {appVersion}{Environment.NewLine}Time: {now:O}{Environment.NewLine}{Environment.NewLine}{redactor.Redact(exception.ToString())}";
        File.WriteAllText(path, text);
        return path;
    }
}
