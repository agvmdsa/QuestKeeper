using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqwCompanion.Phone.Devices;

public sealed record StoredDevice(string Id, string Name, string TokenHash, DateTimeOffset CreatedUtc, DateTimeOffset LastSeenUtc);

public sealed record PhoneFile(bool Enabled, IReadOnlyList<StoredDevice> Devices)
{
    public static PhoneFile Empty { get; } = new(false, []);
}

public interface IPhoneFileStore
{
    /// <summary>The saved file; a missing or damaged file is <see cref="PhoneFile.Empty"/>. Never throws.</summary>
    PhoneFile Load();

    /// <summary>Saves the file. A failure to write is not fatal and does not throw.</summary>
    void Save(PhoneFile file);

    /// <summary>Deletes the saved file. A failure is not fatal and does not throw.</summary>
    void Delete();
}

public sealed record PairedDevice(string Id, string Name, DateTimeOffset CreatedUtc, DateTimeOffset LastSeenUtc);

public enum PairStatus { Paired, Invalid, Locked }

public sealed record PairResult(PairStatus Status, string? DeviceToken = null);

public sealed record PairBody(string? Token, string? Name);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(PhoneFile))]
[JsonSerializable(typeof(PairBody))]
internal sealed partial class PhoneFileJsonContext : JsonSerializerContext;

public static class PhoneFileJson
{
    public static string Serialize(PhoneFile file) => JsonSerializer.Serialize(file, PhoneFileJsonContext.Default.PhoneFile);

    public static bool TryParse(string json, [NotNullWhen(true)] out PhoneFile? file)
    {
        try
        {
            file = JsonSerializer.Deserialize(json, PhoneFileJsonContext.Default.PhoneFile);
        }
        catch (JsonException)
        {
            file = null;
        }

        if (file is { Devices: null })
        {
            file = file with { Devices = [] };
        }

        return file is not null;
    }

    public static bool TryParsePair(string json, [NotNullWhen(true)] out PairBody? body)
    {
        try
        {
            body = JsonSerializer.Deserialize(json, PhoneFileJsonContext.Default.PairBody);
        }
        catch (JsonException)
        {
            body = null;
        }

        return body is not null;
    }
}

/// <summary>
/// "Phone access" on/off, one-time pairing tokens and the paired devices. A device token is shown once and only its SHA-256 is kept.
/// </summary>
public sealed class PhoneRegistry
{
    public static readonly TimeSpan PairingTokenLife = TimeSpan.FromMinutes(5);
    private const int MaxOutstanding = 5;
    private const int MaxFailures = 5;
    private const int MaxNameLength = 40;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly IPhoneFileStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly List<StoredDevice> _devices;
    private readonly Dictionary<string, DateTimeOffset> _pairing = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _failures = new();
    private bool _enabled;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public PhoneRegistry(IPhoneFileStore store, Func<DateTimeOffset> now)
    {
        _store = store;
        _now = now;
        var file = store.Load();
        _enabled = file.Enabled;
        _devices = [.. file.Devices ?? []];
    }

    /// <summary>Ids of the devices that were revoked. Raised after the registry lock is released; a throwing handler is ignored.</summary>
    public event Action<IReadOnlyList<string>>? DevicesRevoked;

    /// <summary>A device paired, was revoked, or all were forgotten. Raised after the registry lock is released; a throwing handler is ignored.</summary>
    public event Action? DevicesChanged;

    public bool Enabled
    {
        get
        {
            lock (_lock)
            {
                return _enabled;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            _enabled = enabled;
            Save();
        }
    }

    public string CreatePairingToken()
    {
        lock (_lock)
        {
            var now = _now();
            foreach (var expired in _pairing.Where(p => p.Value <= now).Select(p => p.Key).ToList())
            {
                _pairing.Remove(expired);
            }

            while (_pairing.Count >= MaxOutstanding)
            {
                _pairing.Remove(_pairing.MinBy(p => p.Value).Key);
            }

            var token = NewToken(32);
            _pairing[token] = now + PairingTokenLife;
            return token;
        }
    }

    public PairResult TryPair(string? pairingToken, string? deviceName)
    {
        PairResult result;
        lock (_lock)
        {
            var now = _now();
            if (now < _lockedUntil)
            {
                return new PairResult(PairStatus.Locked);
            }

            if (pairingToken is null || !_pairing.Remove(pairingToken, out var expires) || expires <= now)
            {
                Fail(now);
                return new PairResult(PairStatus.Invalid);
            }

            var deviceToken = NewToken(32);
            _devices.Add(new StoredDevice(NewToken(6), CleanName(deviceName), Hash(deviceToken), now, now));
            Save();
            result = new PairResult(PairStatus.Paired, deviceToken);
        }

        Raise(null, changed: true);
        return result;
    }

    public PairedDevice? Authenticate(string? deviceToken)
    {
        if (string.IsNullOrEmpty(deviceToken))
        {
            return null;
        }

        var hash = Hash(deviceToken);
        lock (_lock)
        {
            var index = _devices.FindIndex(d => d.TokenHash == hash);
            if (index < 0)
            {
                return null;
            }

            var now = _now();
            var device = _devices[index];
            if (now - device.LastSeenUtc >= SeenEvery)
            {
                device = device with { LastSeenUtc = now };
                _devices[index] = device;
                Save();
            }

            return ToPublic(device);
        }
    }

    public IReadOnlyList<PairedDevice> List()
    {
        lock (_lock)
        {
            return [.. _devices.Select(ToPublic)];
        }
    }

    public bool Revoke(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _devices.RemoveAll(d => d.Id == id) > 0;
            if (removed)
            {
                Save();
            }
        }

        if (removed)
        {
            Raise([id], changed: true);
        }

        return removed;
    }

    public int RevokeAll()
    {
        IReadOnlyList<string> ids;
        lock (_lock)
        {
            ids = ClearDevices();
        }

        if (ids.Count > 0)
        {
            Raise(ids, changed: true);
        }

        return ids.Count;
    }

    /// <summary>
    /// "Forget my data": revokes every device, deletes the saved file and drops pairing tokens and failures.
    /// <see cref="Enabled"/> stays as the player set it.
    /// </summary>
    public void Forget()
    {
        IReadOnlyList<string> ids;
        lock (_lock)
        {
            ids = ClearDevices(save: false);
            _pairing.Clear();
            _failures.Clear();
            _lockedUntil = DateTimeOffset.MinValue;
            _store.Delete();
        }

        Raise(ids.Count > 0 ? ids : null, changed: true);
    }

    private IReadOnlyList<string> ClearDevices(bool save = true)
    {
        var ids = _devices.Select(d => d.Id).ToList();
        _devices.Clear();
        if (save && ids.Count > 0)
        {
            Save();
        }

        return ids;
    }

    private void Raise(IReadOnlyList<string>? revoked, bool changed)
    {
        if (revoked is { Count: > 0 })
        {
            try
            {
                DevicesRevoked?.Invoke(revoked);
            }
            catch (Exception)
            {
                // A listener must never break the registry.
            }
        }

        if (changed)
        {
            try
            {
                DevicesChanged?.Invoke();
            }
            catch (Exception)
            {
                // A listener must never break the registry.
            }
        }
    }

    private void Fail(DateTimeOffset now)
    {
        while (_failures.Count > 0 && now - _failures.Peek() >= FailureWindow)
        {
            _failures.Dequeue();
        }

        _failures.Enqueue(now);
        if (_failures.Count >= MaxFailures)
        {
            _lockedUntil = now + LockFor;
            _failures.Clear();
        }
    }

    private void Save() => _store.Save(new PhoneFile(_enabled, [.. _devices]));

    private static PairedDevice ToPublic(StoredDevice d) => new(d.Id, d.Name, d.CreatedUtc, d.LastSeenUtc);

    private static string NewToken(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray()).Trim();
        if (clean.Length > MaxNameLength)
        {
            var cut = MaxNameLength;
            if (char.IsHighSurrogate(clean[cut - 1]))
            {
                cut--;
            }

            clean = clean[..cut].TrimEnd();
        }

        return clean.Length == 0 ? "Browser" : clean;
    }
}
