namespace AqwCompanion.Tests.Architecture;

public class PhoneIsolationTests
{
    private static readonly string[] Forbidden = ["Avalonia", "WebView", "SharpPcap", "PacketDotNet", "LiteDB", "AqwCompanion.App", "AqwCompanion.Capture"];

    [Fact]
    public void The_phone_assembly_references_only_core_among_our_assemblies()
    {
        var referenced = typeof(AqwCompanion.Phone.PhoneAssembly).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.Contains("AqwCompanion.Core", referenced);
        Assert.DoesNotContain(referenced, name => Forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_phone_project_file_has_no_ui_capture_or_storage_packages_and_no_app_reference()
    {
        var csproj = File.ReadAllText(FindProject());

        Assert.Contains("AqwCompanion.Core.csproj", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", csproj, StringComparison.Ordinal);
        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, csproj.Replace("AqwCompanion.Core.csproj", ""), StringComparison.OrdinalIgnoreCase);
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
