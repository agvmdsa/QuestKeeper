using System.Globalization;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Capture;

/// <summary>
/// The thread-safe counters of one live capture run. Holds numbers and exception type names only: a device name, an address
/// or anything from a packet never goes in, so <see cref="Describe"/> is safe to show and to share.
/// </summary>
public sealed class LiveCaptureStats : ICaptureDiagnostics
{
    /// <summary>The most distinct exception type names kept per kind of failure.</summary>
    public const int MaxFailureTypes = 5;

    private readonly object _gate = new();
    private readonly List<string> _openFailures = [];
    private string? _driver;
    private long _found;
    private long _loopback;
    private long _considered;
    private long _opened;
    private long _failed;
    private long _packets;
    private long _parsed;
    private long _gamePort;

    /// <summary>Records how many devices the driver listed.</summary>
    public void DevicesFound(int count) => Interlocked.Add(ref _found, count);

    /// <summary>Records a device skipped because it is a loopback device.</summary>
    public void LoopbackSkipped() => Interlocked.Increment(ref _loopback);

    /// <summary>Records a non-loopback device the source tried to open.</summary>
    public void Considered() => Interlocked.Increment(ref _considered);

    /// <summary>Records a device that opened and started.</summary>
    public void Opened() => Interlocked.Increment(ref _opened);

    /// <summary>Records a device that could not be opened; only the exception type name is kept.</summary>
    public void OpenFailed(Exception exception)
    {
        Interlocked.Increment(ref _failed);
        var name = exception.GetType().Name;
        lock (_gate)
        {
            if (_openFailures.Count < MaxFailureTypes && !_openFailures.Contains(name))
            {
                _openFailures.Add(name);
            }
        }
    }

    /// <summary>Records a packet handed over by the driver, before any parsing.</summary>
    public void PacketFromDriver() => Interlocked.Increment(ref _packets);

    /// <summary>Records a packet accepted as IPv4/TCP.</summary>
    public void PacketParsed() => Interlocked.Increment(ref _parsed);

    /// <summary>Records an accepted packet whose ports are game ports.</summary>
    public void PacketOnGamePort() => Interlocked.Increment(ref _gamePort);

    /// <summary>Records that the driver is not available; with an exception only its type name is kept.</summary>
    public void DriverUnavailable(Exception? exception = null)
    {
        var text = exception is null ? "not found" : "not found (" + exception.GetType().Name + ")";
        lock (_gate)
        {
            _driver = text;
        }
    }

    /// <inheritdoc />
    public string Describe()
    {
        try
        {
            string? driver;
            string failures;
            lock (_gate)
            {
                driver = _driver;
                failures = _openFailures.Count == 0 ? string.Empty : " (" + string.Join(", ", _openFailures) + ")";
            }

            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Devices: {Read(ref _found)} found, {Read(ref _loopback)} loopback skipped, {Read(ref _considered)} tried, {Read(ref _opened)} opened, {Read(ref _failed)} failed to open{failures}. Packets from the driver: {Read(ref _packets)}, parsed as TCP: {Read(ref _parsed)}, game port: {Read(ref _gamePort)}.");
            return driver is null ? text : "Driver: " + driver + ". " + text;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static long Read(ref long field) => Interlocked.Read(ref field);
}
