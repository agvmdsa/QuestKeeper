using AqwCompanion.Core.Account;

namespace AqwCompanion.App.SignIn;

public enum SignInOutcome
{
    Connected,
    Cancelled,
    Failed,
}

/// <summary>
/// How the sign-in window ended. <see cref="CharacterName"/> and <see cref="Summary"/> are set only when connected
/// (the summary may still be null when the page did not carry it).
/// </summary>
public sealed record SignInResult(
    SignInOutcome Outcome,
    string? CharacterName = null,
    string? Message = null,
    AccountSummary? Summary = null)
{
    public static SignInResult Connected(string characterName, AccountSummary? summary = null) =>
        new(SignInOutcome.Connected, characterName, Summary: summary);

    public static SignInResult Cancelled() => new(SignInOutcome.Cancelled);

    public static SignInResult Failed(string message) => new(SignInOutcome.Failed, Message: message);
}
