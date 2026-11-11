namespace AqwCompanion.Tests.Architecture;

public class PhoneIsolationTests
{
    private static readonly string[] Forbidden = ["Avalonia", "WebView", "SharpPcap", "PacketDotNet", "LiteDB", "AqwCompanion.App", "AqwCompanion.Capture"];

    private static readonly string[] AllowedProjects = ["AqwCompanion.Core", "AqwCompanion.Skua"];

    [Fact]
    public void The_phone_assembly_references_only_core_and_skua_among_our_assemblies()
    {
        var referenced = typeof(AqwCompanion.Phone.PhoneAssembly).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.Contains("AqwCompanion.Core", referenced);
        Assert.Equal(
            [],
            referenced.Where(n => n.StartsWith("AqwCompanion", StringComparison.OrdinalIgnoreCase) && !AllowedProjects.Contains(n, StringComparer.Ordinal)).ToList());
        Assert.DoesNotContain(referenced, name => Forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_phone_project_file_has_no_ui_capture_or_storage_packages_and_no_app_reference()
    {
        var csproj = File.ReadAllText(FindProject());

        var projects = System.Text.RegularExpressions.Regex.Matches(csproj, "<ProjectReference\\s+Include=\"([^\"]+)\"")
            .Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(AllowedProjects, projects);
        Assert.DoesNotContain("PackageReference", csproj, StringComparison.Ordinal);
        var rest = csproj.Replace("AqwCompanion.Core.csproj", "").Replace("AqwCompanion.Skua.csproj", "");
        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, rest, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindProject()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AqwCompanion.Phone", "AqwCompanion.Phone.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("AqwCompanion.Phone.csproj not found above " + AppContext.BaseDirectory);
    }
}
