using AqwCompanion.Phone.Skua;
using Serilog;

namespace AqwCompanion.App.Skua;

/// <summary>A JSON file of SkuaLink's own about Skua scripts (preferences, cached option definitions), written whole and moved into place. Never throws.</summary>
public sealed class SkuaScriptFile(string path) : ISkuaScriptFile
{
    public string? Read()
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Log.Error("Skua script file load failed: {ExceptionType}", ex.GetType().Name);
            return null;
        }
    }

    public void Write(string text)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Log.Error("Skua script file save failed: {ExceptionType}", ex.GetType().Name);
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (IsExpected(cleanup))
            {
                // The temp file is harmless; the next save overwrites it.
            }
        }
    }

    public void Delete()
    {
        foreach (var file in new[] { path, path + ".tmp" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                Log.Error("Skua script file delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException;
}
