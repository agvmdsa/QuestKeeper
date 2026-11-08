using System.Text.Json;
using AqwCompanion.Core.Persistence;
using LiteDB;
using Serilog;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>
/// Stores JSON payloads in a single LiteDB file. The database is opened per operation in shared mode, so another
/// running copy of the app never blocks this one. A file that is not a valid database is set aside once.
/// </summary>
public sealed class LiteDbStoredDataStore(string filePath) : IStoredDataStore
{
    private const string CollectionName = "records";

    public StoredValue<T>? Load<T>(string key, int version)
    {
        var record = Run(db => db.GetCollection<StoredRecord>(CollectionName).FindById(key));
        if (record is null || record.SchemaVersion != version)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(record.Payload, StoredJson.Options) is { } value
                ? new StoredValue<T>(value, DateTimeOffset.FromUnixTimeMilliseconds(record.ReadAtUnixMs))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public void Save<T>(string key, int version, T value, DateTimeOffset readAtUtc) =>
        Run(db => db.GetCollection<StoredRecord>(CollectionName).Upsert(new StoredRecord
        {
            Id = key,
            SchemaVersion = version,
            ReadAtUnixMs = readAtUtc.ToUnixTimeMilliseconds(),
            Payload = JsonSerializer.Serialize(value, StoredJson.Options),
        }));

    public void Remove(string key) =>
        Run(db => db.GetCollection<StoredRecord>(CollectionName).Delete(key));

    public bool Clear()
    {
        var allRemoved = true;
        foreach (var path in new[] { filePath, LogFilePath, CorruptFilePath })
        {
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // The data directory does not exist, so there is nothing to delete.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                allRemoved = false;
                Log.Error("Deleting a stored data file failed: {ExceptionType}", ex.GetType().Name);
            }
        }

        return allRemoved;
    }

    private string LogFilePath =>
        Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath) + "-log" + Path.GetExtension(filePath));

    private string CorruptFilePath => filePath + ".corrupt";

    private T Run<T>(Func<LiteDatabase, T> action)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        LiteDatabase db;
        try
        {
            db = Open();
        }
        catch (Exception ex) when (IsCorruptDatabase(ex))
        {
            Quarantine();
            db = Open();
        }

        // The action runs outside the corruption scope, so its own exceptions never move a healthy file aside.
        using (db)
        {
            return action(db);
        }
    }

    // LiteDB reports a bad header either as INVALID_DATABASE or, for garbage bytes that fail date decoding in the header page, as ArgumentOutOfRangeException.
    private static bool IsCorruptDatabase(Exception ex) =>
        ex is LiteException { ErrorCode: LiteException.INVALID_DATABASE } or ArgumentOutOfRangeException;

    /// <summary>Opens the database and forces the header read, so a bad file fails here and not inside the operation.</summary>
    private LiteDatabase Open()
    {
        var db = new LiteDatabase(new ConnectionString { Filename = filePath, Connection = ConnectionType.Shared });
        try
        {
            _ = db.UserVersion;
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    // A stale log next to the bad file must not be replayed into the fresh one.
    private void Quarantine()
    {
        File.Delete(LogFilePath);
        File.Move(filePath, CorruptFilePath, overwrite: true);
    }
}
