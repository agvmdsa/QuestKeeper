namespace AqwCompanion.App.SignIn;

public enum SignInOutcome
{
    Connected,
    Cancelled,
    Failed,
}

/// <summary>How the sign-in window ended. <see cref="CharacterName"/> is set only when connected.</summary>
public sealed record SignInResult(SignInOutcome Outcome, string? CharacterName = null, string? Message = null)
{
    public static SignInResult Connected(string characterName) => new(SignInOutcome.Connected, characterName);

    public static SignInResult Cancelled() => new(SignInOutcome.Cancelled);

    public static SignInResult Failed(string message) => new(SignInOutcome.Failed, Message: message);
}
