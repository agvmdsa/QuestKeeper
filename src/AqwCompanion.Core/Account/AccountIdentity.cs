namespace AqwCompanion.Core.Account;

public sealed record AccountIdentity(string CharacterName, AccountSummary? Summary = null);
