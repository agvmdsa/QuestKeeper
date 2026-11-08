using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Capture;

public class LiveCaptureStatsTests
{
    private sealed class CustomFailure(string message) : Exception(message);

    [Fact]
    public void A_new_instance_describes_zeros_and_is_a_capture_diagnostics()
    {
        ICaptureDiagnostics stats = new LiveCaptureStats();

        Assert.Equal(
            "Devices: 0 found, 0 loopback skipped, 0 tried, 0 opened, 0 failed to open. Packets from the driver: 0, parsed as TCP: 0, game port: 0.",
            stats.Describe());
    }

    [Fact]
    public void Describe_has_the_documented_format()
    {
        var stats = new LiveCaptureStats();
        stats.DevicesFound(5);
        stats.LoopbackSkipped();
        for (var i = 0; i < 4; i++)
        {
            stats.Considered();
        }

        stats.Opened();
        stats.Opened();
        stats.OpenFailed(new InvalidOperationException("secret device text"));
        stats.OpenFailed(new InvalidOperationException("again"));
        for (var i = 0; i < 120; i++)
        {
            stats.PacketFromDriver();
        }

        for (var i = 0; i < 118; i++)
        {
            stats.PacketParsed();
        }

        for (var i = 0; i < 40; i++)
        {
            stats.PacketOnGamePort();
        }

        Assert.Equal(
            "Devices: 5 found, 1 loopback skipped, 4 tried, 2 opened, 2 failed to open (InvalidOperationException). Packets from the driver: 120, parsed as TCP: 118, game port: 40.",
            stats.Describe());
    }

    [Fact]
    public void Counters_survive_many_threads()
    {
        var stats = new LiveCaptureStats();

        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            for (var i = 0; i < 5000; i++)
            {
                stats.PacketFromDriver();
                stats.PacketParsed();
                stats.PacketOnGamePort();
                stats.OpenFailed(new InvalidOperationException());
            }
        });

        var text = stats.Describe();
        Assert.Contains("40000 failed to open (InvalidOperationException)", text);
        Assert.Contains("Packets from the driver: 40000, parsed as TCP: 40000, game port: 40000.", text);
    }

    [Fact]
    public void At_most_five_distinct_failure_type_names_are_kept()
    {
        var stats = new LiveCaptureStats();
        Exception[] failures =
        [
            new InvalidOperationException(), new ArgumentException(), new CustomFailure("x"), new FormatException(),
            new TimeoutException(), new NotSupportedException(), new ArgumentException(),
        ];
        foreach (var failure in failures)
        {
            stats.OpenFailed(failure);
        }

        var text = stats.Describe();

        Assert.Contains("7 failed to open (InvalidOperationException, ArgumentException, CustomFailure, FormatException, TimeoutException).", text);
        Assert.DoesNotContain("NotSupportedException", text);
    }

    [Fact]
    public void A_missing_driver_is_reported_with_the_type_name_only()
    {
        var missing = new LiveCaptureStats();
        missing.DriverUnavailable();
        var notLoaded = new LiveCaptureStats();
        notLoaded.DriverUnavailable(new DllNotFoundException("C:\\fictional\\wpcap.dll"));

        Assert.StartsWith("Driver: not found. Devices: 0 found", missing.Describe());
        Assert.StartsWith("Driver: not found (DllNotFoundException). Devices: 0 found", notLoaded.Describe());
        Assert.DoesNotContain("fictional", notLoaded.Describe());
    }

    [Fact]
    public void No_device_name_address_or_message_text_is_ever_included()
    {
        const string deviceName = "\\Device\\NPF_{FICTIONAL-0000}";
        const string address = "192.0.2.77";
        var stats = new LiveCaptureStats();
        stats.DevicesFound(1);
        stats.Considered();
        stats.OpenFailed(new InvalidOperationException(deviceName + " " + address));
        stats.DriverUnavailable(new BadImageFormatException(deviceName + " " + address));
        stats.PacketFromDriver();

        var text = stats.Describe();

        Assert.DoesNotContain(deviceName, text);
        Assert.DoesNotContain("FICTIONAL", text);
        Assert.DoesNotContain(address, text);
        Assert.DoesNotContain("192", text);
        Assert.Contains("BadImageFormatException", text);
    }

    [Fact]
    public void The_live_source_is_diagnostics_that_describe_without_a_run()
    {
        ICaptureDiagnostics source = new LivePacketSource();

        Assert.StartsWith("Devices: 0 found", source.Describe());
    }
}
