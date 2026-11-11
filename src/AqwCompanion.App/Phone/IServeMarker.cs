using Serilog;

namespace AqwCompanion.App.Phone;

/// <summary>
/// A flag that says "this app published the phone page through Tailscale and has not removed it yet", so a crash can be cleaned up
/// at the next start. It holds no data and is not part of "Forget my data". Implementations never throw.
/// </summary>
public interface IServeMarker
{
    bool Exists();

    void Create();

    void Delete();
}

/// <summary>The marker as an empty file.</summary>
public sealed class FileServeMarker(string path) : IServeMarker
{
    public bool Exists()
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read the phone serve marker: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    public void Create()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not write the phone serve marker: {ExceptionType}", ex.GetType().Name);
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not delete the phone serve marker: {ExceptionType}", ex.GetType().Name);
        }
    }
}
