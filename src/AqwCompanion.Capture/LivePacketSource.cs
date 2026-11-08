using AqwCompanion.Core.Capture;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AqwCompanion.Capture;

/// <summary>
/// The live adapter of the packet source seam: SharpPcap over Npcap, on every non-loopback device that opens, filtered to the
/// game ports. A device that is down or cannot be opened is skipped (an adapter without traffic is harmless). Nothing touches
/// the driver until <see cref="RunAsync"/>, which does nothing when Npcap is not available, and returns at once when no device
/// could be opened so the caller can report it. The callback runs on capture threads (one at a time) and must not throw.
/// </summary>
public sealed class LivePacketSource : IPacketSource
{
    /// <inheritdoc />
    public async Task RunAsync(Action<TcpSegment> onSegment, CancellationToken cancellationToken)
    {
        if (NpcapAvailability.Check() != NpcapStatus.Available)
        {
            return;
        }

        var gate = new object();
        var opened = new List<ILiveDevice>();
        try
        {
            foreach (var device in CaptureDeviceList.Instance)
            {
                if (device is not LibPcapLiveDevice { Loopback: false } live)
                {
                    continue;
                }

                try
                {
                    live.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100 });
                    live.Filter = GameTraffic.CaptureFilter;
                    live.OnPacketArrival += (_, packet) =>
                    {
                        if (FrameParser.TryParse((int)packet.Device.LinkType, packet.Data, out var segment)
                            && (GameTraffic.IsGamePort(segment.SourcePort) || GameTraffic.IsGamePort(segment.DestinationPort)))
                        {
                            lock (gate)
                            {
                                onSegment(segment);
                            }
                        }
                    };
                    live.StartCapture();
                    opened.Add(live);
                }
                catch (Exception ex) when (ex is PcapException or InvalidOperationException or DllNotFoundException)
                {
                    TryClose(live);
                }
            }

            if (opened.Count == 0)
            {
                return;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is PcapException or DllNotFoundException or BadImageFormatException)
        {
        }
        finally
        {
            foreach (var device in opened)
            {
                TryClose(device);
            }
        }
    }

    private static void TryClose(ILiveDevice device)
    {
        try
        {
            device.StopCapture();
        }
        catch (Exception ex) when (ex is PcapException or InvalidOperationException)
        {
        }

        try
        {
            device.Close();
        }
        catch (Exception ex) when (ex is PcapException or InvalidOperationException)
        {
        }
    }
}
