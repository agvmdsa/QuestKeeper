using System.IO.Compression;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Diagnostics;

public sealed class DiagnosticsExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));
    private readonly string _logs;
    private readonly string _output;

    public DiagnosticsExporterTests()
    {
        _logs = Path.Combine(_root, "logs");
        _output = Path.Combine(_root, "out", "diag.zip");
        Directory.CreateDirectory(_logs);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static PageSnapshot Snapshot() => PageSnapshot.Create(
        "https://account.aq.com/AQW/Inventory", "<p>owner@example.com</p>", "1.0.0", DateTimeOffset.UnixEpoch);

    private static string ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"missing {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Export_contains_info_and_redacted_logs_but_no_snapshot_by_default()
    {
        File.WriteAllText(Path.Combine(_logs, "log-20261104.txt"), "user a@b.com logged in");
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(_logs, "9.9.9", _output, Snapshot(), IncludeSnapshot: false));

        using var zip = ZipFile.OpenRead(_output);
        Assert.Contains("9.9.9", ReadEntry(zip, "info.txt"));
        var log = ReadEntry(zip, "logs/log-20261104.txt");
        Assert.DoesNotContain("a@b.com", log);
        Assert.Contains(SnapshotRedactor.Mask, log);
        Assert.Null(zip.GetEntry("snapshot.txt"));
    }

    [Fact]
    public void Export_includes_redacted_snapshot_only_with_opt_in()
    {
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", _output, Snapshot(), IncludeSnapshot: true));

        using var zip = ZipFile.OpenRead(_output);
        var snapshot = ReadEntry(zip, "snapshot.txt");
        Assert.DoesNotContain("owner@example.com", snapshot);
        Assert.Contains(SnapshotRedactor.Mask, snapshot);
    }

    [Fact]
    public void Export_with_opt_in_but_no_snapshot_does_not_create_snapshot_entry()
    {
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", _output, null, IncludeSnapshot: true));

        using var zip = ZipFile.OpenRead(_output);
        Assert.Null(zip.GetEntry("snapshot.txt"));
    }

    [Fact]
    public void Export_keeps_only_the_three_most_recent_logs()
    {
        for (var i = 1; i <= 5; i++)
        {
            var path = Path.Combine(_logs, $"log-{i}.txt");
            File.WriteAllText(path, $"entry {i}");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, i, 0, 0, 0, DateTimeKind.Utc));
        }
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", _output, null, IncludeSnapshot: false));

        using var zip = ZipFile.OpenRead(_output);
        var logEntries = zip.Entries.Where(e => e.FullName.StartsWith("logs/")).Select(e => e.FullName).Order().ToList();
        Assert.Equal(["logs/log-3.txt", "logs/log-4.txt", "logs/log-5.txt"], logEntries);
    }

    [Fact]
    public void Export_succeeds_when_log_directory_is_missing()
    {
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(Path.Combine(_root, "nope"), "1.0.0", _output, null, IncludeSnapshot: false));

        using var zip = ZipFile.OpenRead(_output);
        Assert.NotNull(zip.GetEntry("info.txt"));
    }

    [Fact]
    public void Export_reads_logs_that_another_process_still_has_open()
    {
        var path = Path.Combine(_logs, "log-open.txt");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("still writing"u8);
        writer.Flush();
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", _output, null, IncludeSnapshot: false));

        using var zip = ZipFile.OpenRead(_output);
        Assert.Equal("still writing", ReadEntry(zip, "logs/log-open.txt"));
    }

    [Fact]
    public void Successful_export_leaves_no_temp_file()
    {
        File.WriteAllText(Path.Combine(_logs, "log-1.txt"), "entry");
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", _output, null, IncludeSnapshot: false));

        Assert.True(File.Exists(_output));
        Assert.False(File.Exists(_output + ".tmp"));
    }

    [Fact]
    public void Failed_export_propagates_leaves_no_temp_file_and_keeps_previous_zip()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        var previous = "previous good export"u8.ToArray();
        File.WriteAllBytes(_output, previous);
        // A log held with an exclusive lock makes reading it throw mid-export (Windows and macOS).
        using var locked = new FileStream(
            Path.Combine(_logs, "locked.txt"), FileMode.Create, FileAccess.Write, FileShare.None);
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        Assert.ThrowsAny<Exception>(() =>
            exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", _output, null, IncludeSnapshot: false)));

        Assert.False(File.Exists(_output + ".tmp"));
        Assert.Equal(previous, File.ReadAllBytes(_output));
    }

    [Fact]
    public void Export_refuses_to_overwrite_a_log_file_it_is_exporting()
    {
        var log = Path.Combine(_logs, "log-1.txt");
        File.WriteAllText(log, "precious log");
        var exporter = new DiagnosticsExporter(new SnapshotRedactor());

        Assert.Throws<ArgumentException>(() =>
            exporter.Export(new DiagnosticsRequest(_logs, "1.0.0", log, null, IncludeSnapshot: false)));

        Assert.Equal("precious log", File.ReadAllText(log));
    }
}
