using AqwCompanion.Phone.Devices;
using Serilog;

namespace AqwCompanion.App.Phone;

/// <summary>Keeps the phone access switch and the paired devices as one JSON file, written whole and moved into place. Never throws.</summary>
public sealed class PhoneStore(string path) : IPhoneFileStore
{
    public PhoneFile Load()
    {
        try
        {
            return File.Exists(path) && PhoneFileJson.TryParse(File.ReadAllText(path), out var file) ? file : PhoneFile.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Phone file load failed: {ExceptionType}", ex.GetType().Name);
            return PhoneFile.Empty;
        }
    }

    public void Save(PhoneFile file)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, PhoneFileJson.Serialize(file));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Phone file save failed: {ExceptionType}", ex.GetType().Name);
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temp file is harmless; the next save overwrites it.
            }
        }
    }

    public void Delete()
    {
        // A crashed save can leave the temp file behind, and it holds the same token hashes.
        foreach (var file in new[] { path, path + ".tmp" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Phone file delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
