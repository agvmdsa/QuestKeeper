using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.Architecture;

public class CoreIsolationTests
{
    private static readonly string[] Forbidden = ["Avalonia", "WebView", "Webviews"];

    [Fact]
    public void Core_does_not_reference_ui_or_webview_assemblies()
    {
        var referenced = typeof(PageSnapshot).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced,
            name => Forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Core_project_file_has_no_ui_package_references()
    {
        var csproj = File.ReadAllText(FindCoreProject());

        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, csproj, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindCoreProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AqwCompanion.Core", "AqwCompanion.Core.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("AqwCompanion.Core.csproj not found above " + AppContext.BaseDirectory);
    }
}
