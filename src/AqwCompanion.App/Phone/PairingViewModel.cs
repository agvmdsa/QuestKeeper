using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using AqwCompanion.Phone.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Phone;

/// <summary>A paired phone as the window shows it.</summary>
public sealed record DeviceRow(string Id, string Name, string Created, string LastSeen, ICommand RevokeCommand);

/// <summary>
/// The pairing window: a fresh one-time link and its QR code, how long it lives, and the paired phones with Revoke.
/// The link holds a pairing token, so it is only ever held here and shown in the link text and the QR code; it is never logged.
/// </summary>
public sealed partial class PairingViewModel : ObservableObject, IDisposable
{
    public const string ExplainText = "Scan with your phone's camera. The link works once and expires in 5 minutes.";
    public const string ExpiredText = "This link expired. Create a new one.";
    public const string EmptyDevicesText = "No phones paired yet.";
    public const int PixelsPerModule = 6;

    private static readonly TimeSpan Life = PhoneRegistry.PairingTokenLife;

    private readonly IPairingLinkSource _source;
    private readonly PhoneRegistry _registry;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Action> _post;
    private readonly Func<string, Task>? _copy;
    private DateTimeOffset _createdAt;
    private string? _token;
    private bool _hasLink;
    private bool _disposed;

    /// <param name="post">Runs a refresh on the UI thread; the registry raises its events from any thread (inline by default).</param>
    /// <param name="copyToClipboard">Puts text on the clipboard; null when there is no clipboard.</param>
    public PairingViewModel(IPairingLinkSource source, PhoneRegistry registry, Func<DateTimeOffset> now, Action<Action>? post = null, Func<string, Task>? copyToClipboard = null)
    {
        _source = source;
        _registry = registry;
        _now = now;
        _post = post ?? (action => action());
        _copy = copyToClipboard;
        _registry.DevicesChanged += OnDevicesChanged;
        RefreshDevices();
        Regenerate();
    }

    /// <summary>The pairing link; empty when there is none or it expired.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    [NotifyCanExecuteChangedFor(nameof(CopyLinkCommand))]
    private string _link = string.Empty;

    /// <summary>PNG bytes of the QR code; empty when there is no link.</summary>
    [ObservableProperty]
    private byte[] _qrPng = [];

    /// <summary>What to read instead of the QR code: the Tailscale sentence, or that the link expired.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _expiresText = string.Empty;

    [ObservableProperty]
    private bool _isExpired;

    public bool HasLink => Link.Length > 0;

    public bool HasMessage => Message.Length > 0;

    public ObservableCollection<DeviceRow> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public bool HasNoDevices => Devices.Count == 0;

    /// <summary>Cancels the current link in the registry (it no longer pairs), then makes a new one-time link and its QR code.</summary>
    [RelayCommand]
    public void Regenerate()
    {
        CancelCurrent();
        var offer = _source.CreateOffer();
        if (offer is null)
        {
            _hasLink = false;
            IsExpired = false;
            Link = string.Empty;
            QrPng = [];
            ExpiresText = string.Empty;
            Message = _source.UnavailableText;
            return;
        }

        var link = offer.Link;
        _token = offer.Token;
        byte[] png;
        try
        {
            png = QrImage.Render(link, PixelsPerModule);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not draw the pairing code: {ExceptionType}", ex.GetType().Name);
            png = [];
        }

        _createdAt = _now();
        _hasLink = true;
        IsExpired = false;
        Message = string.Empty;
        Link = link;
        QrPng = png;
        Tick();
    }

    /// <summary>Updates the countdown and, once the link is five minutes old, replaces the code with the expiry notice. Call about once a second.</summary>
    public void Tick()
    {
        if (!_hasLink || _disposed)
        {
            return;
        }

        if (!_source.IsAvailable)
        {
            // Tailscale or the host dropped: the link cannot work any more.
            CancelCurrent();
            _hasLink = false;
            Link = string.Empty;
            QrPng = [];
            ExpiresText = string.Empty;
            Message = _source.UnavailableText;
            return;
        }

        var left = _createdAt + Life - _now();
        if (left <= TimeSpan.Zero)
        {
            CancelCurrent();
            _hasLink = false;
            IsExpired = true;
            Link = string.Empty;
            QrPng = [];
            ExpiresText = string.Empty;
            Message = ExpiredText;
            return;
        }

        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        ExpiresText = string.Create(CultureInfo.InvariantCulture, $"Expires in {seconds / 60}:{seconds % 60:00}");
    }

    [RelayCommand]
    public void Revoke(string id)
    {
        _registry.Revoke(id);
        RefreshDevices();
    }

    [RelayCommand]
    public void RevokeAll()
    {
        _registry.RevokeAll();
        RefreshDevices();
    }

    private bool CanCopy() => HasLink && _copy is not null;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyLink()
    {
        if (_copy is null || Link.Length == 0)
        {
            return;
        }

        try
        {
            await _copy(Link);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not copy the pairing link: {ExceptionType}", ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelCurrent();
        _registry.DevicesChanged -= OnDevicesChanged;
    }

    // A link that was replaced, expired or left behind by a closed window must not stay a live credential. Cancelling a used token is harmless.
    private void CancelCurrent()
    {
        _registry.CancelPairingToken(_token);
        _token = null;
    }

    private void OnDevicesChanged() => _post(() =>
    {
        if (!_disposed)
        {
            RefreshDevices();
        }
    });

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var d in _registry.List())
        {
            var id = d.Id;
            Devices.Add(new DeviceRow(id, d.Name, "Paired " + Format(d.CreatedUtc), "Last seen " + Format(d.LastSeenUtc), new RelayCommand(() => Revoke(id))));
        }

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasNoDevices));
    }

    private static string Format(DateTimeOffset utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
