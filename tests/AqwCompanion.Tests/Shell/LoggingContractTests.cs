using System.IO.Compression;
using AqwCompanion.App;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Extensions.Logging;

namespace AqwCompanion.Tests.Shell;

/// <summary>
/// The contract of the log guard: whatever a call site pushes through the real logger, the crash handler
/// and the Microsoft logging bridge, none of it reaches a file under the application folder or the exported zip.
/// </summary>
[Collection("GlobalLog")]
public sealed class LoggingContractTests : IDisposable
{
    private const string Email = "user@example.com";
    private const string Ip = "203.0.113.7";
    private const string Token = "SECRETTOKEN-0123456789";
    private const string Base64 = "dGhpcyBpcyBhIHNlY3JldCBwYXlsb2FkIDEyMzQ1Njc4OTAhQCMkJV4mKigp";

    private static readonly string[] Secrets = [Email, Ip, Token, Base64];

    private readonly AppPaths _paths =
        new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        Log.Logger = Logger.None;
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    private sealed record Nested(string Contact, string Address, Nested? Child);

    private static InvalidOperationException Boom(string message, Exception? inner = null)
    {
        try
        {
            throw new InvalidOperationException(message, inner);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private void PushSecretsThroughEveryPath()
    {
        var logger = LoggingSetup.CreateLogger(_paths, "9.9.9-contract");
        Log.Logger = logger;

        foreach (var secret in Secrets)
        {
            logger.Warning("plain property {Value}", secret);
            logger.Warning("nested {@Thing}", new Nested(secret, secret, new Nested(secret, secret, null)));
            logger.Warning("sequence {Values}", new[] { secret, secret });
            logger.Warning("dictionary {@Map}", new Dictionary<string, string> { [secret] = secret });
        }

        var message = string.Join(' ', Secrets);
        logger.Error(Boom(message), "with exception");
        logger.Error(Boom("outer", new ArgumentException(message)), "with inner exception {Value}", message);

        using var factory = new SerilogLoggerFactory(logger);
        var bridged = factory.CreateLogger<LoggingContractTests>();
        bridged.LogError(Boom(message), "bridged {Value} {Other}", message, Token);
        bridged.LogWarning("bridged without exception {Value}", Email);

        CrashHandler.Report(Boom(message, new ArgumentException(message)), _paths.CrashDirectory, "9.9.9-contract");

        logger.Dispose();
        Log.Logger = Logger.None;
    }

    private IEnumerable<string> AllTexts() =>
        Directory.GetFiles(_paths.Root, "*", SearchOption.AllDirectories).Select(File.ReadAllText);

    [Fact]
    public void No_secret_reaches_any_log_or_crash_file_but_the_exception_type_does()
    {
        PushSecretsThroughEveryPath();

        var files = Directory.GetFiles(_paths.Root, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(Directory.GetFiles(_paths.LogDirectory));
        Assert.NotEmpty(Directory.GetFiles(_paths.CrashDirectory));
        var texts = AllTexts().ToList();
        Assert.Equal(files.Length, texts.Count);
        foreach (var text in texts)
        {
            foreach (var secret in Secrets)
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }

        var log = string.Concat(Directory.GetFiles(_paths.LogDirectory).Select(File.ReadAllText));
        var crash = string.Concat(Directory.GetFiles(_paths.CrashDirectory).Select(File.ReadAllText));
        Assert.Contains("System.InvalidOperationException", log);
        Assert.Contains("--- inner: System.ArgumentException", log);
        Assert.Contains("System.InvalidOperationException", crash);
        Assert.Contains("bridged without exception", log);
    }

    [Fact]
    public void The_exported_diagnostics_zip_contains_no_secret()
    {
        PushSecretsThroughEveryPath();
        var zipPath = Path.Combine(_paths.ExportDirectory, "contract.zip");

        new DiagnosticsExporter(new SnapshotRedactor()).Export(
            new DiagnosticsRequest(_paths.LogDirectory, "9.9.9-contract", zipPath, null, false));

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.NotEmpty(archive.Entries);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            foreach (var secret in Secrets)
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
    }
}
