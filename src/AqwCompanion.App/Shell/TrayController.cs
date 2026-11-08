using AqwCompanion.App.GameData;

namespace AqwCompanion.App.Shell;

/// <summary>
/// The decisions behind the tray icon: closing the window hides it while the capture keeps running, the menu opens the
/// window, starts or stops the capture and really exits. It knows no Avalonia types.
/// </summary>
public sealed class TrayController
{
    /// <summary>The longest Exit waits for the capture devices to close.</summary>
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    private const string Name = "AQW Companion";

    private readonly CaptureSession _session;
    private readonly IMainWindowHost _window;
    private readonly IApplicationExit _exit;
    private readonly TimeSpan _stopTimeout;
    private int _exiting;

    public TrayController(CaptureSession session, IMainWindowHost window, IApplicationExit exit, TimeSpan? stopTimeout = null)
    {
        _session = session;
        _window = window;
        _exit = exit;
        _stopTimeout = stopTimeout ?? DefaultStopTimeout;
        _session.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The session state or the characters changed (the session's event, re-raised; may come from any thread).</summary>
    public event EventHandler? Changed;

    /// <summary>True once Exit started; from then on a close is never turned into a hide.</summary>
    public bool IsExiting => Volatile.Read(ref _exiting) != 0;

    /// <summary>
    /// <paramref name="userInitiated"/> is true when the user pressed the close button, false for an OS shutdown, an
    /// application shutdown or an owner closing. Returns true when the close must be cancelled because the window was hidden instead.
    /// </summary>
    public bool ShouldCancelClose(bool userInitiated)
    {
        if (!userInitiated || IsExiting)
        {
            return false;
        }

        _window.Hide();
        return true;
    }

    /// <summary>Shows the window.</summary>
    public void Open() => _window.Show();

    /// <summary>Stops a listening capture; otherwise starts one without waiting for it to end.</summary>
    public void ToggleCapture()
    {
        if (_session.State == CaptureState.Listening)
        {
            _session.Stop();
        }
        else
        {
            _ = StartAsync();
        }
    }

    /// <summary>Stops the capture (waiting at most the timeout for the devices to close), then exits. Safe to call twice.</summary>
    public async Task ExitAsync()
    {
        if (Interlocked.Exchange(ref _exiting, 1) != 0)
        {
            return;
        }

        await _session.StopAndWaitAsync(_stopTimeout).ConfigureAwait(false);
        _exit.Exit();
    }

    /// <summary>The tray tooltip: the product name and what the capture is doing.</summary>
    public string ToolTip => Name + " - " + StateText();

    /// <summary>The text of the capture menu item.</summary>
    public string CaptureMenuText => _session.State == CaptureState.Listening ? "Stop capture" : "Start capture";

    private async Task StartAsync()
    {
        try
        {
            await _session.StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The session reports its own failures through its state.
        }
    }

    private string StateText()
    {
        switch (_session.State)
        {
            case CaptureState.Listening:
                var captured = _session.Characters.Count(c => _session.IsCapturedThisSession(c.Profile.Name));
                return captured switch
                {
                    0 => "waiting for a login",
                    1 => "capturing, 1 character",
                    _ => "capturing, " + captured.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters",
                };
            case CaptureState.NpcapMissing:
                return "Npcap is not installed";
            case CaptureState.NeedsAdministrator:
                return "needs administrator rights";
            case CaptureState.Failed:
                return "capture failed";
            case CaptureState.NotSupported:
                return "capture not available";
            default:
                return "not capturing";
        }
    }
}
