using System.Globalization;

namespace AqwCompanion.Core.Site;

/// <summary>Addresses of the account.aq.com pages the companion reads.</summary>
public static class AqwUrls
{
    public const string AccountHost = "account.aq.com";

    public const string Root = "https://account.aq.com/";
    public const string Home = "https://account.aq.com/Home";
    public const string BuySeasonal = "https://account.aq.com/AQW/BuySeasonal";
    public const string InventoryAwesome = "https://account.aq.com/AQW/InventoryAwesome";
    public const string BuyBack = "https://account.aq.com/AQW/BuyBack";
    public const string Progress = "https://account.aq.com/AQW/Progress";
    public const string ProgressPlaytime = "https://account.aq.com/AQW/ProgressPlaytime";
    public const string ProgressSagas = "https://account.aq.com/AQW/ProgressSagas";
    public const string ProgressMembership = "https://account.aq.com/AQW/ProgressMembership";
    public const string ProgressCoins = "https://account.aq.com/AQW/ProgressCoins";
    /// <summary>The plain link behind the site's own "Logout" menu item; opened only when the user confirms Sign out.</summary>
    public const string Exit = "https://account.aq.com/Login/Exit";
    public const string CharPageBase = "https://account.aq.com/CharPage";

    public static string CharPage(string characterName) =>
        $"{CharPageBase}?id={Uri.EscapeDataString(characterName)}";

    /// <summary>The public badges list of a character, addressed by the numeric id the character page carries.</summary>
    public static string Badges(int ccid) =>
        string.Create(CultureInfo.InvariantCulture, $"{CharPageBase}/Badges?ccid={ccid}");

    /// <summary>True for an absolute https URL on the account host whose path is <c>/Home</c> (case-insensitive).</summary>
    public static bool IsAccountHome(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAccountPage(uri, "/Home");

    /// <summary>True for an absolute https URL on the account host whose path is <c>/CharPage</c> (case-insensitive).</summary>
    public static bool IsCharPage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAccountPage(uri, "/CharPage");

    /// <summary>True for an absolute https URL on the account host whose path is <c>/CharPage/Badges</c> (case-insensitive).</summary>
    public static bool IsBadges(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAccountPage(uri, "/CharPage/Badges");

    internal static bool IsAccountPage(Uri uri, string path) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, AccountHost, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);
}
