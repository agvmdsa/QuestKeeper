using System.Xml.Linq;

namespace AqwCompanion.Tests.Architecture;

/// <summary>The parchment palette must keep readable text on every surface it is drawn on (WCAG 2.x contrast).</summary>
public class AqwThemeTests
{
    private const double Normal = 4.5;

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("AqwText")]
    [InlineData("AqwTextBright")]
    [InlineData("AqwTextMuted")]
    [InlineData("AqwTextSecondary")]
    [InlineData("AqwLink")]
    [InlineData("AqwDangerText")]
    public void Text_colors_are_readable_on_the_page_the_paper_and_the_darker_band(string text)
    {
        var colors = Colors();

        foreach (var surface in new[] { "AqwWindowBackground", "AqwSurface", "AqwSurfaceRaised" })
        {
            Assert.True(
                Contrast(colors[text + "Color"], colors[surface + "Color"]) >= Normal,
                $"{text} on {surface} is below {Normal}:1");
        }
    }

    [Theory]
    [InlineData("AqwAccentText", "AqwAccent")]
    [InlineData("AqwSuccessText", "AqwSuccess")]
    [InlineData("AqwOnTopBar", "AqwTopBar")]
    [InlineData("AqwSuccess", "AqwSurface")]
    public void Text_on_its_own_fill_is_readable(string text, string fill)
    {
        var colors = Colors();

        Assert.True(Contrast(colors[text + "Color"], colors[fill + "Color"]) >= Normal, $"{text} on {fill} is below {Normal}:1");
    }

    [Fact]
    public void The_window_background_is_the_parchment_of_the_aqw_site()
    {
        Assert.Equal("#FEF0C2", Colors()["AqwWindowBackgroundColor"], ignoreCase: true);
    }

    private static Dictionary<string, string> Colors() =>
        Load("Themes", "AqwTheme.axaml").Root!
            .Elements()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => e.Value.Trim());

    private static double Contrast(string a, string b)
    {
        var (light, dark) = (Luminance(a), Luminance(b));
        if (light < dark)
        {
            (light, dark) = (dark, light);
        }

        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(string hex)
    {
        var rgb = new[] { 1, 3, 5 }
            .Select(i => Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0)
            .Select(c => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4))
            .ToArray();
        return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
    }

    private static XDocument Load(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, "src", "AqwCompanion.App", .. relative]);
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(Path.Combine(relative) + " not found above " + AppContext.BaseDirectory);
    }
}
