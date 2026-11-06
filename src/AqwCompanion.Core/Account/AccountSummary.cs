namespace AqwCompanion.Core.Account;

/// <summary>
/// The few account fields shown on the Account card; each is null when the page did not carry it.
/// <see cref="MembershipStatus"/> is the page's own line, date included.
/// </summary>
public sealed record AccountSummary(int? AdventureCoins, string? AccountType, string? MembershipStatus, int? GearsOfDoom);
