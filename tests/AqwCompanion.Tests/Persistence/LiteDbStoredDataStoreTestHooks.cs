using LiteDB;

namespace AqwCompanion.Tests.Persistence;

internal static class LiteDbStoredDataStoreTestHooks
{
    public static void SetSchemaVersion(string file, string id, int version)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = file, Connection = ConnectionType.Shared });
        var records = db.GetCollection("records");
        var doc = records.FindById(id);
        doc["SchemaVersion"] = version;
        records.Update(doc);
    }

    public static void SetReadAt(string file, string id, long unixMs)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = file, Connection = ConnectionType.Shared });
        var records = db.GetCollection("records");
        var doc = records.FindById(id);
        doc["ReadAtUnixMs"] = unixMs;
        records.Update(doc);
    }
}
