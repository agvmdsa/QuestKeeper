namespace AqwCompanion.Skua;

/// <summary>
/// A script in Skua's scripts folder. <paramref name="Id"/> is the only thing the app ever sends back about it.
/// <paramref name="Title"/>, <paramref name="Description"/> and <paramref name="Tags"/> come from the script's header
/// comment (see <see cref="SkuaScriptHeader"/>); null when it has none.
/// </summary>
public sealed record SkuaScriptInfo(string Id, string Name, string Folder, string? Title = null, string? Description = null, string? Tags = null);

/// <summary>What one read of a script file told the plugin: whether it is a bot, and its header comment data.</summary>
public sealed record SkuaScriptFacts(bool IsBot, string? Title, string? Description, string? Tags);

/// <summary>
/// An option a script declares. <paramref name="Type"/> is one of <see cref="SkuaProtocol.OptionTypes"/>;
/// <paramref name="Choices"/> holds the members of an enum option (null when not reported). Two definitions are equal when
/// their choices hold the same entries in the same order.
/// </summary>
public sealed record SkuaOptionDef(
    string Category, string Name, string DisplayName, string Description, string Type, string Default, IReadOnlyList<string>? Choices = null)
{
    public bool Equals(SkuaOptionDef? other) =>
        other is not null
        && Category == other.Category
        && Name == other.Name
        && DisplayName == other.DisplayName
        && Description == other.Description
        && Type == other.Type
        && Default == other.Default
        && SkuaLists.Same(Choices, other.Choices);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Category);
        hash.Add(Name);
        hash.Add(DisplayName);
        hash.Add(Description);
        hash.Add(Type);
        hash.Add(Default);
        SkuaLists.Add(ref hash, Choices);
        return hash.ToHashCode();
    }
}

/// <summary>A saved option value, as a string the way Skua's options file holds it.</summary>
public sealed record SkuaOptionValue(string Category, string Name, string Value);

/// <summary>A command from the app to the plugin. <paramref name="ReqId"/> pairs it with its <see cref="ResultMessage"/>.</summary>
public abstract record SkuaCommand(string ReqId) : SkuaMessage;

public sealed record ListScriptsCommand(string ReqId) : SkuaCommand(ReqId);

public sealed record GetOptionsCommand(string ReqId, string ScriptId) : SkuaCommand(ReqId);

/// <summary>Two commands are equal when their values hold the same entries in the same order.</summary>
public sealed record SetOptionsCommand(string ReqId, string ScriptId, IReadOnlyList<SkuaOptionValue> Values) : SkuaCommand(ReqId)
{
    public bool Equals(SetOptionsCommand? other) =>
        other is not null && base.Equals(other) && ScriptId == other.ScriptId && SkuaLists.Same(Values, other.Values);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(ScriptId);
        SkuaLists.Add(ref hash, Values);
        return hash.ToHashCode();
    }
}

public sealed record StartCommand(string ReqId, string ScriptId) : SkuaCommand(ReqId);

public sealed record StopCommand(string ReqId) : SkuaCommand(ReqId);

/// <summary>
/// The plugin's one answer to a command. Each list is null when the command does not report it and empty when there is
/// nothing; two results are equal when their lists hold the same entries in the same order. <paramref name="Truncated"/>
/// says the script list was cut (by <see cref="SkuaProtocol.MaxScripts"/> or by the result line cap).
/// </summary>
public sealed record ResultMessage(
    string ReqId, bool Ok, string? Error, IReadOnlyList<SkuaScriptInfo>? Scripts, IReadOnlyList<SkuaOptionDef>? OptionDefs,
    IReadOnlyList<SkuaOptionValue>? Values, bool HasOptions, bool FirstRun, bool Truncated = false) : SkuaMessage
{
    public bool Equals(ResultMessage? other) =>
        other is not null
        && base.Equals(other)
        && ReqId == other.ReqId
        && Ok == other.Ok
        && Error == other.Error
        && SkuaLists.Same(Scripts, other.Scripts)
        && SkuaLists.Same(OptionDefs, other.OptionDefs)
        && SkuaLists.Same(Values, other.Values)
        && HasOptions == other.HasOptions
        && FirstRun == other.FirstRun
        && Truncated == other.Truncated;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ReqId);
        hash.Add(Ok);
        hash.Add(Error);
        SkuaLists.Add(ref hash, Scripts);
        SkuaLists.Add(ref hash, OptionDefs);
        SkuaLists.Add(ref hash, Values);
        hash.Add(HasOptions);
        hash.Add(FirstRun);
        hash.Add(Truncated);
        return hash.ToHashCode();
    }
}

/// <summary>Content equality for the lists the messages carry (records compare lists by reference otherwise).</summary>
internal static class SkuaLists
{
    public static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));

    public static void Add<T>(ref HashCode hash, IReadOnlyList<T>? list)
    {
        hash.Add(list?.Count ?? -1);
        foreach (var item in list ?? [])
        {
            hash.Add(item);
        }
    }
}
