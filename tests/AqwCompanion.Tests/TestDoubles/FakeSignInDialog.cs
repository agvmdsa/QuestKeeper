using AqwCompanion.App.SignIn;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeSignInDialog : ISignInDialog
{
    public List<SignInMode> Shown { get; } = [];

    /// <summary>What the dialog reports; defaults to the user cancelling.</summary>
    public Func<SignInMode, Task<SignInResult>> OnShow { get; set; } = _ => Task.FromResult(SignInResult.Cancelled());

    public Task<SignInResult> ShowAsync(SignInMode mode)
    {
        Shown.Add(mode);
        return OnShow(mode);
    }
}
