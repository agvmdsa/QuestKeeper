using Serilog;

namespace AqwCompanion.App.Reputation;

/// <summary>
/// Opens reputation.json in the app's reputation folder. The file is opened so that the writing client is not blocked, and
/// failures are logged by exception type only and reported as a status.
/// </summary>
public sealed class ReputationFileSource(AppPaths paths) : IReputationAutoSource
{
    public const string FileName = "reputation.json";

    public string Path { get; } = System.IO.Path.Combine(paths.ReputationDirectory, FileName);

    public ReputationAutoRead Open()
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
            var read = new ReputationAutoRead(
                ReputationAutoStatus.Found,
                new ReputationAutoFile(stream, stream.Length, new DateTimeOffset(File.GetLastWriteTimeUtc(Path), TimeSpan.Zero)));
            stream = null;
            return read;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ReputationAutoRead(ReputationAutoStatus.Missing);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Opening the reputation file failed: {ExceptionType}", ex.GetType().Name);
            return new ReputationAutoRead(ReputationAutoStatus.Unreadable);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    public void Delete()
    {
        try
        {
            if (Directory.Exists(paths.ReputationDirectory))
            {
                Directory.Delete(paths.ReputationDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Deleting the reputation file failed: {ExceptionType}", ex.GetType().Name);
        }
    }
}
