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
        var outputPath = Path.GetFullPath(request.OutputPath);
        var tempPath = outputPath + ".tmp";
        var logs = RecentLogs(request.LogDirectory).ToList();
        RejectOutputThatIsALog(logs, outputPath, tempPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        try
        {
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var snapshot = request.IncludeSnapshot ? request.FailingSnapshot : null;

                WriteText(zip, "info.txt", BuildInfo(request, snapshot));

                foreach (var logFile in logs)
                {
                    WriteText(zip, $"logs/{Path.GetFileName(logFile)}", redactor.Redact(ReadShared(logFile)));
                }

                if (snapshot is not null)
                {
                    WriteText(zip, "snapshot.txt", redactor.Redact(snapshot.Payload));
                }
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        return request.OutputPath;
    }

    private static void RejectOutputThatIsALog(IEnumerable<string> logs, string outputPath, string tempPath)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var log in logs.Select(Path.GetFullPath))
        {
            if (string.Equals(log, outputPath, comparison) || string.Equals(log, tempPath, comparison))
            {
                throw new ArgumentException("The output path must not be one of the log files being exported.", nameof(outputPath));
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
