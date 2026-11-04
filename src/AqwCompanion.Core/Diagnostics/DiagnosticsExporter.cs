using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Core.Diagnostics;

public sealed record DiagnosticsRequest(
    string LogDirectory,
    string AppVersion,
    string OutputPath,
    PageSnapshot? FailingSnapshot,
    bool IncludeSnapshot);

public sealed class DiagnosticsExporter(SnapshotRedactor redactor)
{
    private const int MaxLogFiles = 3;

    public string Export(DiagnosticsRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);

        using var file = new FileStream(request.OutputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        var snapshot = request.IncludeSnapshot ? request.FailingSnapshot : null;

        WriteText(zip, "info.txt", BuildInfo(request, snapshot));

        foreach (var logFile in RecentLogs(request.LogDirectory))
        {
            WriteText(zip, $"logs/{Path.GetFileName(logFile)}", redactor.Redact(ReadShared(logFile)));
        }

        if (snapshot is not null)
        {
            WriteText(zip, "snapshot.txt", redactor.Redact(snapshot.Payload));
        }

        return request.OutputPath;
    }

    private static string BuildInfo(DiagnosticsRequest request, PageSnapshot? snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AppVersion: {request.AppVersion}");
        sb.AppendLine($"GeneratedAt: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"SnapshotIncluded: {(snapshot is null ? "no" : "yes")}");
        if (snapshot is not null)
        {
            sb.AppendLine($"SnapshotUrl: {snapshot.Url}");
            sb.AppendLine($"SnapshotCapturedAt: {snapshot.CapturedAt:O}");
        }
        return sb.ToString();
    }

    private static IEnumerable<string> RecentLogs(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(MaxLogFiles)
            : [];

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
