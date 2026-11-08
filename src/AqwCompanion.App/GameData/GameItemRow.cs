namespace AqwCompanion.App.GameData;

/// <summary>One owned item of the Game data list, with the numbers already formatted. Flags look like "AC · Member · Equipped · Bank".</summary>
public sealed record GameItemRow(string Name, string Type, string Quantity, string Level, string Flags);
