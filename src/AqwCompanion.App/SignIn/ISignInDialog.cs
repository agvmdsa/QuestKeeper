namespace AqwCompanion.App.SignIn;

/// <summary>The modal window in which the user signs in on account.aq.com so the app can read the character name.</summary>
public interface ISignInDialog
{
    /// <summary>Shows the window and completes when it has closed. It never throws; failures come back as a result.</summary>
    Task<SignInResult> ShowAsync(SignInMode mode);
}
