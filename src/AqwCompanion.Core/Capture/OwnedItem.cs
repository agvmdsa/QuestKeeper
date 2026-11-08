namespace AqwCompanion.Core.Capture;

/// <summary>One item the Character owns, with only the Allow-listed fields.</summary>
public sealed record OwnedItem(string Name, string Type, int Quantity, int Stack, int Level, ItemFlags Flags, int ItemId, int? ClassId);
