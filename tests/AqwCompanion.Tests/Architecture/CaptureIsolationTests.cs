using AqwCompanion.App;
using AqwCompanion.Capture;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Architecture;

public class CaptureIsolationTests
{
    private static readonly string[] CaptureLibraries = ["SharpPcap", "PacketDotNet"];

    [Fact]
    public void Core_does_not_reference_the_capture_libraries()
    {
        AssertAssemblyHasNone(typeof(PcapStreamSource).Assembly);
        AssertProjectHasNone(FindProject("src", "AqwCompanion.Core"));
    }

    [Fact]
    public void App_does_not_reference_the_capture_libraries()
    {
        AssertAssemblyHasNone(typeof(AppPaths).Assembly);
        AssertProjectHasNone(FindProject("src", "AqwCompanion.App"));
    }

    [Fact]
    public void Capture_project_references_core_and_not_the_app()
    {
        var csproj = File.ReadAllText(FindProject("src", "AqwCompanion.Capture"));

        Assert.Contains("AqwCompanion.Core.csproj", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("AqwCompanion.App", csproj, StringComparison.OrdinalIgnoreCase);

        var referenced = typeof(LivePacketSource).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
        Assert.Contains("AqwCompanion.Core", referenced);
        Assert.DoesNotContain(referenced, name => name.Contains("AqwCompanion.App", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_two_adapters_of_the_seam_live_in_their_own_assemblies()
    {
        Assert.Equal("AqwCompanion.Capture", typeof(LivePacketSource).Assembly.GetName().Name);
        Assert.Equal("AqwCompanion.Core", typeof(PcapStreamSource).Assembly.GetName().Name);
        Assert.True(typeof(IPacketSource).IsAssignableFrom(typeof(LivePacketSource)));
        Assert.True(typeof(IPacketSource).IsAssignableFrom(typeof(PcapStreamSource)));
    }

    private static void AssertAssemblyHasNone(System.Reflection.Assembly assembly)
    {
        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(referenced,
            name => CaptureLibraries.Any(l => name.Contains(l, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertProjectHasNone(string csprojPath)
    {
        var csproj = File.ReadAllText(csprojPath);

        foreach (var library in CaptureLibraries)
        {
            Assert.DoesNotContain(library, csproj, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindProject(string folder, string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, folder, name, name + ".csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name + ".csproj not found above " + AppContext.BaseDirectory);
    }
}
