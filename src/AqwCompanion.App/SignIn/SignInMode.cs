namespace AqwCompanion.App.SignIn;

/// <summary>Which layout of the sign-in window to open.</summary>
public enum SignInMode
{
    /// <summary>The browser is visible so the user can sign in: first sign-in, or an expired session.</summary>
    Full,

    /// <summary>A small progress window for refreshing an existing session; the browser is not shown.</summary>
    Compact,
}
