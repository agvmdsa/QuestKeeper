using System.Xml.Linq;

namespace AqwCompanion.Tests.Architecture;

/// <summary>
/// The Game data capture opens network devices and starts by itself when the app opens, so the executable must ask Windows for
/// administrator rights in its manifest. Losing that line would silently turn the capture into "needs administrator".
/// </summary>
public class AppManifestTests
{
    private static readonly XNamespace V3 = "urn:schemas-microsoft-com:asm.v3";

    [Fact]
    public void The_manifest_asks_for_administrator_rights()
    {
        var level = Manifest()
            .Descendants(V3 + "requestedExecutionLevel")
            .Select(e => (string?)e.Attribute("level"))
            .SingleOrDefault();

        Assert.Equal("requireAdministrator", level);
    }

    [Fact]
    public void The_manifest_keeps_the_windows_10_support_the_embedded_browser_needs()
    {
        var ids = Manifest()
            .Descendants(XName.Get("supportedOS", "urn:schemas-microsoft-com:compatibility.v1"))
            .Select(e => (string?)e.Attribute("Id"))
            .ToList();

        Assert.Contains("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", ids);
    }

    private static XDocument Manifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AqwCompanion.App", "app.manifest");
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("app.manifest not found above " + AppContext.BaseDirectory);
    }
}
