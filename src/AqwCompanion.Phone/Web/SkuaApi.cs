using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Web;

/// <summary>
/// The phone's Skua routes under <c>/api/skua</c>. <see cref="PhoneHandler"/> has already checked the host, the origin, the
/// write header (<c>X-Remote: 1</c> on every POST) and the device cookie. Here, in this order: the route (404), the method
/// (405), for a POST the content type (415, <c>application/json</c>), the body size (413; <see cref="MaxBody"/>, options
/// <see cref="MaxOptionsBody"/>) and the body (400), the script id (400, only <c>[0-9a-f]{16}</c>), the instance (404 unknown,
/// 409 disconnected), one state-changing command per instance at a time (409 <c>busy</c>), then the command: no answer in time is 504 <c>Skua did not answer</c>, a refusal by the plugin is 400
/// with the plugin's text. Every error body is <c>{"error":"..."}</c>.
/// </summary>
/// <remarks>
/// Option values: the phone sees and sends them as the plugin's definitions describe them (doubles invariant, enum members as
/// shown). The plugin answers raw file values when it has no definitions (Skua restarted); they are normalised against the
/// definitions SkuaLink caches (<see cref="SkuaOptionCache"/>), and before a <c>setOptions</c> the plugin is asked
/// (<c>getOptions</c>) whether it knows the definitions, so doubles go to a plugin without them in the PC's culture (the
/// plugin then writes the text as it is). Types are checked here from the definitions (the plugin's, else the cached ones);
/// a plugin without definitions checks only that the names are in the file.
/// </remarks>
public sealed class SkuaApi(ISkuaFeed feed, ISkuaCommandChannel channel, SkuaScriptPrefs prefs, SkuaOptionCache cache, CultureInfo? fileCulture = null)
{
    public const string Prefix = "/api/skua";

    /// <summary>The body cap of every Skua POST except the options.</summary>
    public const int MaxBody = 1024;

    /// <summary>The body cap of <c>POST .../options</c>.</summary>
    public const int MaxOptionsBody = 4096;

    public const string UnknownSkua = "unknown Skua";
    public const string DisconnectedSkua = "Skua is disconnected";
    public const string NoAnswer = "Skua did not answer";
    public const string InvalidScriptId = "invalid script id";
    public const string InvalidBody = "invalid body";
    public const string NotJson = "body must be json";
    public const string TooLarge = "body too large";
    public const string Refused = "Skua refused the command";
    public const string Busy = "busy";

    private const int MaxInstanceId = SkuaProtocol.MaxTextLength;
    private static readonly JsonDocumentOptions BodyOptions = new() { MaxDepth = 8 };

    private readonly CultureInfo _culture = fileCulture ?? CultureInfo.CurrentCulture;
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);

    /// <summary>True for every path this class answers.</summary>
    public static bool Handles(string path) => path == Prefix || path.StartsWith(Prefix + "/", StringComparison.Ordinal);

    /// <summary>True for a request that sends a command and may wait for Skua (a start up to 45 s); the host gives it longer.</summary>
    public static bool IsCommand(string method, string path) =>
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) && path.StartsWith(Prefix + "/", StringComparison.Ordinal);

    public async Task<PhoneResponse> HandleAsync(PhoneRequest request, string path, CancellationToken ct)
    {
        var method = request.Method.ToUpperInvariant();
        if (path == Prefix)
        {
            return method == "GET" ? Instances() : PhoneHandler.Error(405, "method not allowed");
        }

        var parts = path[(Prefix.Length + 1)..].Split('/');
        var route = parts switch
        {
            [_, "scripts"] => Route.Scripts,
            [_, "scripts", _, "options"] => Route.Options,
            [_, "scripts", _, "prefs"] => Route.Prefs,
            [_, "start"] => Route.Start,
            [_, "stop"] => Route.Stop,
            _ => Route.None,
        };
        if (route == Route.None)
        {
            return PhoneHandler.Error(404, "not found");
        }

        var allowed = route switch
        {
            Route.Scripts => method == "GET",
            Route.Options => method is "GET" or "POST",
            _ => method == "POST",
        };
        if (!allowed)
        {
            return PhoneHandler.Error(405, "method not allowed");
        }

        JsonElement? body = null;
        if (method == "POST")
        {
            if (Body(request, route == Route.Options ? MaxOptionsBody : MaxBody, optional: route == Route.Stop, out body) is { } refused)
            {
                return refused;
            }
        }

        var instanceId = Uri.UnescapeDataString(parts[0]);
        var scriptId = parts.Length > 2 ? parts[2] : null;
        if (scriptId is not null && !SkuaOptionCache.IsScriptId(scriptId))
        {
            return PhoneHandler.Error(400, InvalidScriptId);
        }

        return (route, method) switch
        {
            (Route.Scripts, _) => await ScriptsAsync(instanceId, ct).ConfigureAwait(false),
            (Route.Options, "GET") => await GetOptionsAsync(instanceId, scriptId!, ct).ConfigureAwait(false),
            (Route.Options, _) => await GatedAsync(instanceId, () => SetOptionsAsync(instanceId, scriptId!, body!.Value, ct)).ConfigureAwait(false),
            (Route.Prefs, _) => SetPrefs(instanceId, scriptId!, body!.Value),
            (Route.Start, _) => await GatedAsync(instanceId, () => StartAsync(instanceId, body!.Value, ct)).ConfigureAwait(false),
            _ => await GatedAsync(instanceId, () => StopAsync(instanceId, ct)).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// One state-changing command (start, stop, setOptions) at a time per instance: another one while it is pending is 409
    /// <c>busy</c> and sends nothing. The gate opens whatever happens (answer, refusal, timeout, cancellation).
    /// </summary>
    private async Task<PhoneResponse> GatedAsync(string instanceId, Func<Task<PhoneResponse>> command)
    {
        lock (_busy)
        {
            if (!_busy.Add(instanceId))
            {
                return PhoneHandler.Error(409, Busy);
            }
        }

        try
        {
            return await command().ConfigureAwait(false);
        }
        finally
        {
            lock (_busy)
            {
                _busy.Remove(instanceId);
            }
        }
    }

    private PhoneResponse Instances() =>
        Json(JsonSerializer.Serialize(new SkuaInstancesBody(SkuaInstanceState.From(feed.Current)), SkuaApiJson.Default.SkuaInstancesBody));

    private async Task<PhoneResponse> ScriptsAsync(string instanceId, CancellationToken ct)
    {
        var (result, error) = await SendAsync(instanceId, new ListScriptsCommand(""), ct).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var scripts = result!.Scripts ?? [];
        var ids = scripts.Select(s => s.Id).ToList();
        var ask = prefs.AskOptions(ids);
        cache.Touch(ids);
        var items = scripts.Select(s => new SkuaScriptItem(s.Id, s.Name, s.Folder, ask[s.Id])).ToList();
        return Json(JsonSerializer.Serialize(new SkuaScriptsBody(items, result.Truncated), SkuaApiJson.Default.SkuaScriptsBody));
    }

    private async Task<PhoneResponse> GetOptionsAsync(string instanceId, string scriptId, CancellationToken ct)
    {
        var (result, error) = await SendAsync(instanceId, new GetOptionsCommand("", scriptId), ct).ConfigureAwait(false);
        return error ?? Json(JsonSerializer.Serialize(Options(scriptId, result!), SkuaApiJson.Default.SkuaOptionsBody));
    }

    private async Task<PhoneResponse> SetOptionsAsync(string instanceId, string scriptId, JsonElement body, CancellationToken ct)
    {
        if (!TryReadValues(body, out var values))
        {
            return PhoneHandler.Error(400, InvalidBody);
        }

        // Checked before anything is sent: the rules that need no type (at most 200 characters, which the command writer would
        // otherwise drop silently; no line break; no trailing space) and one value per option.
        if (Duplicate(values) is { } duplicate)
        {
            return PhoneHandler.Error(400, duplicate);
        }

        if (SkuaOptionsFile.Validate(SkuaScriptOptions.Untyped(values), values) is { } invalid)
        {
            return PhoneHandler.Error(400, invalid);
        }

        if (Lookup(instanceId) is { } missing)
        {
            return missing;
        }

        // Does the plugin know the definitions? A plugin without them writes the text as it is (doubles in the PC's culture).
        var (current, error) = await SendAsync(instanceId, new GetOptionsCommand("", scriptId), ct).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var raw = current!.OptionDefs is null;
        if (!raw)
        {
            cache.Set(scriptId, current.OptionDefs!);
        }

        var defs = current.OptionDefs ?? cache.Get(scriptId);
        var outgoing = values;
        if (defs is not null)
        {
            var tidy = SkuaOptionValues.Tidy(defs, values);
            if (SkuaOptionsFile.Validate(defs, tidy) is { } wrong)
            {
                return PhoneHandler.Error(400, wrong);
            }

            outgoing = SkuaOptionValues.ForPlugin(defs, tidy, raw, _culture);
        }

        var (result, failed) = await SendAsync(instanceId, new SetOptionsCommand("", scriptId, outgoing), ct).ConfigureAwait(false);
        return failed ?? Json(JsonSerializer.Serialize(Options(scriptId, result!), SkuaApiJson.Default.SkuaOptionsBody));
    }

    private PhoneResponse SetPrefs(string instanceId, string scriptId, JsonElement body)
    {
        if (!body.TryGetProperty("askOptions", out var ask) || ask.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return PhoneHandler.Error(400, InvalidBody);
        }

        if (Lookup(instanceId) is { } missing)
        {
            return missing;
        }

        prefs.Set(scriptId, ask.GetBoolean());
        return Json(JsonSerializer.Serialize(new SkuaPrefsBody(scriptId, prefs.AskOptions(scriptId)), SkuaApiJson.Default.SkuaPrefsBody));
    }

    private async Task<PhoneResponse> StartAsync(string instanceId, JsonElement body, CancellationToken ct)
    {
        if (!body.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return PhoneHandler.Error(400, InvalidBody);
        }

        var scriptId = idElement.GetString();
        if (!SkuaOptionCache.IsScriptId(scriptId))
        {
            return PhoneHandler.Error(400, InvalidScriptId);
        }

        var (result, error) = await SendAsync(instanceId, new StartCommand("", scriptId!), ct).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        if (result!.OptionDefs is { } learnt)
        {
            cache.Set(scriptId!, learnt);
        }

        var defs = result.OptionDefs ?? cache.Get(scriptId!);
        var values = SkuaOptionValues.FromPlugin(defs, result.Values ?? [], raw: result.OptionDefs is null, _culture);
        return Json(JsonSerializer.Serialize(new SkuaStartBody(result.HasOptions, result.FirstRun, defs, values), SkuaApiJson.Default.SkuaStartBody));
    }

    private async Task<PhoneResponse> StopAsync(string instanceId, CancellationToken ct)
    {
        var (_, error) = await SendAsync(instanceId, new StopCommand(""), ct).ConfigureAwait(false);
        return error ?? Json(JsonSerializer.Serialize(new SkuaOkBody(true), SkuaApiJson.Default.SkuaOkBody));
    }

    /// <summary>A getOptions or setOptions answer merged with the cached definitions.</summary>
    private SkuaOptionsBody Options(string scriptId, ResultMessage result)
    {
        var raw = result.OptionDefs is null;
        if (!raw)
        {
            cache.Set(scriptId, result.OptionDefs!);
        }

        var defs = result.OptionDefs ?? cache.Get(scriptId);
        var fileValues = result.Values ?? [];
        var values = SkuaOptionValues.FromPlugin(defs, fileValues, raw, _culture);
        var hasOptions = defs is { } d ? d.Count > 0 : result.HasOptions || values.Count > 0;
        return new SkuaOptionsBody(defs, values, defs is not null, hasOptions, raw && fileValues.Count == 0, prefs.AskOptions(scriptId));
    }

    /// <summary>Null when the instance is known and connected; else the 404 or 409.</summary>
    private PhoneResponse? Lookup(string instanceId)
    {
        var instance = instanceId.Length is > 0 and <= MaxInstanceId
            ? feed.Current.Instances.FirstOrDefault(i => i.InstanceId == instanceId)
            : null;
        return instance is null ? PhoneHandler.Error(404, UnknownSkua)
            : instance.Disconnected ? PhoneHandler.Error(409, DisconnectedSkua)
            : null;
    }

    private async Task<(ResultMessage? Result, PhoneResponse? Error)> SendAsync(string instanceId, SkuaCommand command, CancellationToken ct)
    {
        if (Lookup(instanceId) is { } missing)
        {
            return (null, missing);
        }

        ResultMessage? result;
        try
        {
            result = await channel.SendCommandAsync(instanceId, command, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return (null, PhoneHandler.Error(404, UnknownSkua));
        }
        catch (InvalidOperationException)
        {
            return (null, PhoneHandler.Error(409, DisconnectedSkua));
        }

        if (result is null)
        {
            return (null, PhoneHandler.Error(504, NoAnswer));
        }

        return result.Ok ? (result, null) : (null, PhoneHandler.Error(400, string.IsNullOrWhiteSpace(result.Error) ? Refused : result.Error));
    }

    /// <summary>Null when the body may be read; else the 415, 413 or 400. An optional body may be empty (then no element).</summary>
    private static PhoneResponse? Body(PhoneRequest request, int cap, bool optional, out JsonElement? body)
    {
        body = null;
        if (request.Body.Length == 0 && optional)
        {
            return null;
        }

        if (!IsJson(request))
        {
            return PhoneHandler.Error(415, NotJson);
        }

        if (Encoding.UTF8.GetByteCount(request.Body) > cap)
        {
            return PhoneHandler.Error(413, TooLarge);
        }

        try
        {
            using var doc = JsonDocument.Parse(request.Body, BodyOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return PhoneHandler.Error(400, InvalidBody);
            }

            body = doc.RootElement.Clone();
            return null;
        }
        catch (JsonException)
        {
            return PhoneHandler.Error(400, InvalidBody);
        }
    }

    private static bool IsJson(PhoneRequest request)
    {
        var type = request.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        if (type is null)
        {
            return false;
        }

        var media = type.Split(';', 2)[0].Trim();
        return media.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadValues(JsonElement body, out IReadOnlyList<SkuaOptionValue> values)
    {
        values = [];
        if (!body.TryGetProperty("values", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<SkuaOptionValue>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || Text(item, "category") is not { Length: > 0 and <= SkuaProtocol.MaxTextLength } category
                || Text(item, "name") is not { Length: > 0 and <= SkuaProtocol.MaxTextLength } name
                || Text(item, "value") is not { } value)
            {
                return false;
            }

            list.Add(new SkuaOptionValue(category, name, value));
        }

        values = list;
        return true;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Duplicate(IReadOnlyList<SkuaOptionValue> values)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var value in values)
        {
            if (!seen.Add((value.Category, value.Name)))
            {
                var label = value.Category == SkuaOptionsFile.DefaultCategory ? value.Name : $"{value.Category}:{value.Name}";
                return $"option {label} given twice";
            }
        }

        return null;
    }

    private static PhoneResponse Json(string json) => PhoneHandler.Json(200, json);

    private enum Route { None, Scripts, Options, Prefs, Start, Stop }
}

/// <summary>Answer of <c>GET /api/skua</c>.</summary>
public sealed record SkuaInstancesBody(IReadOnlyList<SkuaInstanceState> Instances);

public sealed record SkuaScriptItem(string Id, string Name, string Folder, bool AskOptions);

/// <summary>Answer of <c>GET /api/skua/{instanceId}/scripts</c>.</summary>
public sealed record SkuaScriptsBody(IReadOnlyList<SkuaScriptItem> Scripts, bool Truncated);

/// <summary>
/// Answer of <c>GET</c> and <c>POST /api/skua/{instanceId}/scripts/{scriptId}/options</c>. <paramref name="Defs"/>: the
/// plugin's definitions, else SkuaLink's cached ones, else null (<paramref name="Known"/> false: the values, if any, are as the
/// options file holds them). An empty list: the script has no options. <paramref name="Discovery"/>: the plugin knows neither
/// definitions nor an options file, so the next start is a first run with the defaults.
/// </summary>
public sealed record SkuaOptionsBody(
    IReadOnlyList<SkuaOptionDef>? Defs, IReadOnlyList<SkuaOptionValue> Values, bool Known, bool HasOptions, bool Discovery, bool AskOptions);

/// <summary>Answer of <c>POST /api/skua/{instanceId}/start</c>.</summary>
public sealed record SkuaStartBody(bool HasOptions, bool FirstRun, IReadOnlyList<SkuaOptionDef>? Defs, IReadOnlyList<SkuaOptionValue> Values);

/// <summary>Answer of <c>POST /api/skua/{instanceId}/stop</c>.</summary>
public sealed record SkuaOkBody(bool Ok);

/// <summary>Answer of <c>POST /api/skua/{instanceId}/scripts/{scriptId}/prefs</c>.</summary>
public sealed record SkuaPrefsBody(string Id, bool AskOptions);

public sealed record ErrorBody(string Error);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SkuaInstancesBody))]
[JsonSerializable(typeof(SkuaScriptsBody))]
[JsonSerializable(typeof(SkuaOptionsBody))]
[JsonSerializable(typeof(SkuaStartBody))]
[JsonSerializable(typeof(SkuaOkBody))]
[JsonSerializable(typeof(SkuaPrefsBody))]
[JsonSerializable(typeof(ErrorBody))]
internal sealed partial class SkuaApiJson : JsonSerializerContext
{
}
