using AqwCompanion.App;
using AqwCompanion.App.GameData;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A real <see cref="CaptureSession"/> over fakes; change the settings before the first start.</summary>
public sealed class CaptureRig
{
    public FakeProcessElevation Elevation { get; } = new();

    public FakeApplicationExit Exit { get; } = new();

    public FakeGameDataStore Store { get; } = new();

    public NpcapStatus Npcap { get; set; } = NpcapStatus.Available;

    public bool IsWindows { get; set; } = true;

    public FakePacketSource Source { get; set; } = new((_, _) => Task.CompletedTask);

    public CaptureSession Session { get; }

    public CaptureRig() => Session = new CaptureSession(
        () => Source, Elevation, () => Npcap, () => IsWindows, Store, TimeProvider.System, Exit,
        new AppPaths("/tmp/aqw-fictional-root"));

    /// <summary>A source that hands over the segments and ends at once.</summary>
    public static FakePacketSource Feeding(IEnumerable<TcpSegment> segments) =>
        new((onSegment, _) =>
        {
            foreach (var segment in segments)
            {
                onSegment(segment);
            }

            return Task.CompletedTask;
        });

    public GameDataViewModel CreateViewModel(Action<Action>? onUiThread = null) => new(Session, onUiThread);
}
