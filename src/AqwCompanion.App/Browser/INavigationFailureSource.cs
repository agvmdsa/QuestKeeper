namespace AqwCompanion.App.Browser;

/// <summary>
/// Implemented by a browser host that can tell a navigation failed (offline, DNS, TLS), so the sign-in flow
/// does not wait for a page that will never load. Raised on the UI thread; carries no address.
/// </summary>
public interface INavigationFailureSource
{
    event EventHandler? NavigationFailed;
}
