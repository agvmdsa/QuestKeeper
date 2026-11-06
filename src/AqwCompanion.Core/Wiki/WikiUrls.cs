using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Wiki;

/// <summary>Addresses of the AQW Wiki pages the companion reads. Every page constant lives here.</summary>
public static class WikiUrls
{
    public const string Host = "aqwwiki.wikidot.com";
    public const string Root = "https://aqwwiki.wikidot.com/";

    public const string RobotsSlug = "robots.txt";
    public const string HardFarmSlug = "syfarm";

    public const string ClassesSlug = "classes";
    public const string ArmorsSlug = "armors";
    public const string WeaponsSlug = "weapons";
    public const string HelmetsSlug = "helmets-hoods";
    public const string CapesSlug = "capes-back-items";
    public const string PetsSlug = "pets";
    public const string BattlePetsSlug = "battle-pets";
    public const string HousesSlug = "houses";
    public const string FloorItemsSlug = "floor-items";
    public const string WallItemsSlug = "wall-items";
    public const string MiscItemsSlug = "misc-items";
    public const string UseItemsSlug = "use-items";
    public const string NecklacesSlug = "necklaces";
    public const string GroundsSlug = "grounds";
    public const string EnhancementsSlug = "enhancements";

    /// <summary>The longest page name accepted.</summary>
    public const int MaxSlugLength = 100;

    // robots.txt is the one fixed address that is not a page name (it has a dot).
    public static readonly string Robots = Root + RobotsSlug;
    public static readonly string HardFarm = Page(HardFarmSlug);

    /// <summary>The type index pages, in the order they are fetched; <see cref="ClassesSlug"/> comes first.</summary>
    public static readonly IReadOnlyList<string> TypeIndexSlugs =
    [
        ClassesSlug, ArmorsSlug, WeaponsSlug, HelmetsSlug, CapesSlug, PetsSlug, BattlePetsSlug, HousesSlug,
        FloorItemsSlug, WallItemsSlug, MiscItemsSlug, UseItemsSlug, NecklacesSlug, GroundsSlug, EnhancementsSlug,
    ];

    /// <summary>
    /// True for a page name that is safe to put in an address: 1 to <see cref="MaxSlugLength"/> characters, the first a lower-case
    /// letter or digit, the rest lower-case letters, digits, '_' or '-'. Nothing else (no '/', '.', '?', '#', '%', ':', '@',
    /// white space, control or non-ASCII characters) is accepted, because page names come from third-party content.
    /// </summary>
    public static bool IsValidSlug([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? slug)
    {
        if (slug is null || slug.Length == 0 || slug.Length > MaxSlugLength)
        {
            return false;
        }

        for (var i = 0; i < slug.Length; i++)
        {
            var c = slug[i];
            var alnum = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!(alnum || (i > 0 && c is '_' or '-')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The https address of a wiki page; throws <see cref="ArgumentException"/> for a slug that is not <see cref="IsValidSlug"/>.</summary>
    public static string Page(string slug) =>
        TryPage(slug, out var url) ? url : throw new ArgumentException("The wiki page name is not valid.", nameof(slug));

    /// <summary>The https address of a wiki page, or false (and an empty <paramref name="url"/>) for an invalid slug.</summary>
    public static bool TryPage(string? slug, out string url)
    {
        if (IsValidSlug(slug))
        {
            url = Root + slug;
            return true;
        }

        url = string.Empty;
        return false;
    }

    /// <summary>True only for exactly <c>https://aqwwiki.wikidot.com/</c> followed by a valid slug: the shape <see cref="Page"/> builds.</summary>
    public static bool IsSafePageUrl(string? url) =>
        url is not null
        && url.StartsWith(Root, StringComparison.Ordinal)
        && TryPage(url[Root.Length..], out var rebuilt)
        && string.Equals(rebuilt, url, StringComparison.Ordinal);

    public static bool IsTypeIndex(string slug) => TypeIndexSlugs.Contains(slug, StringComparer.Ordinal);

    /// <summary>True for an absolute https URL on the wiki host (default port, no user info).</summary>
    public static bool IsWikiPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && IsWikiHost(uri);

    /// <summary>
    /// The page name (the single path segment, lower-case) of <c>http(s)://aqwwiki.wikidot.com/slug</c> or <c>/slug</c>;
    /// null for another host, a deeper path, an empty path or anything else. Query and fragment are ignored.
    /// </summary>
    public static string? Slug(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var text = url.Trim();
        string path;
        if (text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal))
        {
            path = text;
        }
        else if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && IsWikiHost(uri))
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return null;
        }

        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0)
        {
            path = path[..end];
        }

        var segment = path.Trim('/');
        if (segment.Length == 0 || segment.Contains('/'))
        {
            return null;
        }

        try
        {
            segment = Uri.UnescapeDataString(segment);
        }
        catch (UriFormatException)
        {
            return null;
        }

        return segment.Length == 0 || segment.Contains('/') ? null : segment.ToLowerInvariant();
    }

    private static bool IsWikiHost(Uri uri) =>
        uri.Host.EqualsIgnoreCase(Host) && uri.IsDefaultPort && uri.UserInfo.Length == 0;
}
