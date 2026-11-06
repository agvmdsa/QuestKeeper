using AqwCompanion.Core.Wiki;

namespace AqwCompanion.App.HardFarms;

/// <summary>One line of the Hard Farms list.</summary>
public sealed record HardFarmRow(string Slug, string Name, WikiType Type, string Group, Ownership Ownership, string WikiUrl)
{
    public string TypeText => Type.ToString();

    /// <summary>The badge text; the state is never told by colour alone.</summary>
    public string OwnershipText => Ownership.ToString();

    public bool IsOwned => Ownership == Ownership.Owned;

    public bool IsMissing => Ownership == Ownership.Missing;

    public bool IsUnknown => Ownership == Ownership.Unknown;
}

/// <summary>A choice of a filter drop-down; a null <see cref="Value"/> is the "All" choice. Shown through <see cref="Label"/>.</summary>
public sealed record FilterOption<T>(T? Value, string Label) where T : struct
{
    public override string ToString() => Label;
}

/// <summary>A choice of the group drop-down; a null <see cref="Name"/> is "All groups".</summary>
public sealed record GroupOption(string? Name, string Label)
{
    public override string ToString() => Label;
}
