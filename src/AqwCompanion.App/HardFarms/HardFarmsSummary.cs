namespace AqwCompanion.App.HardFarms;

/// <summary>Owned of total over the whole Hard Farms list, and the display names of the entries not owned.</summary>
public sealed record HardFarmsSummary(int Owned, int Total, IReadOnlyList<string> Missing);
