using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaScriptCatalogTests
{
    // Never touched on disk: the catalog only sees the paths the fake listing returns.
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skualink-catalog-test", "Scripts"));

    private static string At(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static SkuaScriptCatalog Catalog(IEnumerable<string> relative, Func<string, bool>? isLink = null) =>
        Catalog(() => relative.Select(At).ToList(), isLink);

    private static SkuaScriptCatalog Catalog(Func<IEnumerable<string>> files, Func<string, bool>? isLink = null) =>
        new(Root, root => root == Root ? files() : throw new InvalidOperationException("listed the wrong folder: " + root), isLink ?? (_ => false));

    [Fact]
    public void Scripts_are_sorted_by_folder_then_name()
    {
        var catalog = Catalog(["Story/b.cs", "zeta.cs", "Farm/Gold.cs", "Farm/apple.cs", "Alpha.cs", "Farm/Sub/x.cs"]);

        var scripts = catalog.Scan();

        Assert.Equal(
            [("", "Alpha"), ("", "zeta"), ("Farm", "apple"), ("Farm", "Gold"), ("Farm/Sub", "x"), ("Story", "b")],
            scripts.Select(s => (s.Folder, s.Name)));
        Assert.False(catalog.Truncated);
    }

    [Fact]
    public void The_id_is_the_first_8_bytes_of_the_sha256_of_the_relative_path_in_lowercase_hex()
    {
        var scripts = Catalog(["Farm/Example.cs", "Example.cs"]).Scan();

        Assert.Equal("50aee7b7d000df03", scripts.Single(s => s.Folder == "").Id);
        Assert.Equal("88177572adbaef2c", scripts.Single(s => s.Folder == "Farm").Id);
    }

    [Fact]
    public void The_id_is_the_same_on_every_scan()
    {
        var catalog = Catalog(["Farm/Example.cs"]);

        Assert.Equal(catalog.Scan().Single().Id, catalog.Scan().Single().Id);
        Assert.Equal(catalog.Scan().Single().Id, Catalog(["Other.cs", "Farm/Example.cs"]).Scan().Single(s => s.Folder == "Farm").Id);
    }

    [Fact]
    public void A_listed_id_resolves_to_its_full_path()
    {
        var catalog = Catalog(["Farm/Example.cs"]);
        var id = catalog.Scan().Single().Id;

        Assert.True(catalog.TryResolve(id, out var fullPath));
        Assert.Equal(At("Farm/Example.cs"), fullPath);
    }

    [Fact]
    public void Uppercase_cs_files_are_listed_and_resolve()
    {
        var catalog = Catalog(["Farm/Loud.CS", "Farm/readme.txt", "Farm/noext", "Farm/x.csx", "Farm/.cs"]);

        var scripts = catalog.Scan();

        Assert.Equal(["Loud"], scripts.Select(s => s.Name));
        Assert.True(catalog.TryResolve(scripts[0].Id, out var fullPath));
        Assert.Equal(At("Farm/Loud.CS"), fullPath);
    }

    [Fact]
    public void Scripts_under_cached_scripts_are_left_out()
    {
        var catalog = Catalog(["Cached-Scripts/Compiled.cs", "cached-scripts/Other.cs", "Cached-Scripts/Deep/x.cs", "Kept.cs"]);

        Assert.Equal(["Kept"], catalog.Scan().Select(s => s.Name));
    }

    [Fact]
    public void Paths_outside_the_root_are_left_out()
    {
        var catalog = Catalog(() =>
        [
            At("../Outside.cs"),
            At("Farm/../../Escaped.cs"),
            Root + "Sibling" + Path.DirectorySeparatorChar + "x.cs",
            Root + ".cs",
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere.cs")),
            "relative.cs",
            "",
            At("Farm/../Inside.cs"),
        ]);

        var scripts = catalog.Scan();

        var only = Assert.Single(scripts);
        Assert.Equal(("", "Inside"), (only.Folder, only.Name));
        Assert.True(catalog.TryResolve(only.Id, out var fullPath));
        Assert.Equal(At("Inside.cs"), fullPath);
    }

    [Fact]
    public void Paths_and_other_text_are_not_ids()
    {
        var catalog = Catalog(["Farm/Example.cs"]);
        catalog.Scan();

        foreach (var notAnId in new[]
                 {
                     "Farm/Example.cs", At("Farm/Example.cs"), "../Farm/Example.cs", "..", "/etc/passwd", @"C:\Windows\x.cs",
                     "88177572ADBAEF2C", " 88177572adbaef2c", "", "88177572adbaef2", null!,
                 })
        {
            Assert.False(catalog.TryResolve(notAnId, out var fullPath));
            Assert.Equal("", fullPath);
        }
    }

    [Fact]
    public void Only_ids_from_the_last_scan_resolve()
    {
        var files = new List<string> { "Old.cs", "Kept.cs" };
        var catalog = Catalog(files);
        var before = catalog.Scan();
        var oldId = before.Single(s => s.Name == "Old").Id;

        files.Remove("Old.cs");
        catalog.Scan();

        Assert.False(catalog.TryResolve(oldId, out _));
        Assert.True(catalog.TryResolve(before.Single(s => s.Name == "Kept").Id, out _));
    }

    [Fact]
    public void Nothing_resolves_before_the_first_scan()
    {
        var catalog = Catalog(["Farm/Example.cs"]);

        Assert.False(catalog.TryResolve("88177572adbaef2c", out _));
    }

    [Fact]
    public void A_linked_file_or_folder_is_refused()
    {
        var linked = new HashSet<string> { At("Linked.cs"), At("Junction") };
        var catalog = Catalog(["Linked.cs", "Junction/Inside.cs", "Junction/Deep/x.cs", "Plain/x.cs"], linked.Contains);
        var scripts = catalog.Scan();

        Assert.False(catalog.TryResolve(scripts.Single(s => s.Name == "Linked").Id, out _));
        Assert.False(catalog.TryResolve(scripts.Single(s => s.Name == "Inside").Id, out _));
        Assert.False(catalog.TryResolve(scripts.Single(s => s.Folder == "Junction/Deep").Id, out _));
        Assert.True(catalog.TryResolve(scripts.Single(s => s.Folder == "Plain").Id, out _));
    }

    [Fact]
    public void A_link_check_that_fails_refuses_the_script()
    {
        var catalog = Catalog(["Farm/x.cs"], _ => throw new IOException("denied"));
        var id = catalog.Scan().Single().Id;

        Assert.False(catalog.TryResolve(id, out var fullPath));
        Assert.Equal("", fullPath);
    }

    [Fact]
    public void The_link_check_does_not_reach_the_root_or_above()
    {
        var asked = new List<string>();
        var catalog = Catalog(["Farm/Sub/x.cs"], p => { asked.Add(p); return false; });

        Assert.True(catalog.TryResolve(catalog.Scan().Single().Id, out _));
        Assert.Equal([At("Farm/Sub/x.cs"), At("Farm/Sub"), At("Farm")], asked);
    }

    [Fact]
    public void The_list_is_cut_at_the_protocol_cap_after_sorting()
    {
        var names = Enumerable.Range(0, SkuaProtocol.MaxScripts + 5).Select(i => $"s{i:D5}.cs").Reverse().ToList();
        var catalog = Catalog(names);

        var scripts = catalog.Scan();

        Assert.Equal(SkuaProtocol.MaxScripts, scripts.Count);
        Assert.Equal("s00000", scripts[0].Name);
        Assert.Equal($"s{SkuaProtocol.MaxScripts - 1:D5}", scripts[^1].Name);
        Assert.True(catalog.Truncated);
        Assert.False(catalog.TryResolve(Catalog([$"s{SkuaProtocol.MaxScripts:D5}.cs"]).Scan().Single().Id, out _));
    }

    [Fact]
    public void A_file_listed_twice_appears_once()
    {
        var catalog = Catalog(["Farm/x.cs", "Farm/x.cs", "Farm/./x.cs"]);

        Assert.Single(catalog.Scan());
    }

    [Fact]
    public void A_root_with_a_trailing_separator_works_the_same()
    {
        var catalog = new SkuaScriptCatalog(Root + Path.DirectorySeparatorChar, _ => [At("Farm/Example.cs")], _ => false);

        var only = Assert.Single(catalog.Scan());
        Assert.Equal("88177572adbaef2c", only.Id);
        Assert.Equal("Farm", only.Folder);
    }

    [Theory]
    [InlineData("CoreBots.cs")]
    [InlineData("CoreFarms.cs")]
    [InlineData("CoreStory.cs")]
    [InlineData("CoreAdvanced.cs")]
    [InlineData("CoreDailies.cs")]
    [InlineData("Core/TaskExtensions.cs")]
    [InlineData("core/TaskExtensions.cs")]
    [InlineData("Hollowborn/CoreHollowborn.cs")]
    [InlineData("Hollowborn/HollowbornChaosEnvoy/CoreHollowbornChaosEnvoy.cs")]
    [InlineData("Evil/NSoD/CoreNSOD.cs")]
    [InlineData("Evil/SDKA/CoreSDKA.cs")]
    [InlineData("Evil/coresdka.cs")]
    [InlineData("Story/Core/Deep/Anything.cs")]
    public void Library_files_are_not_listed(string relative)
    {
        Assert.Empty(Catalog([relative]).Scan());
    }

    [Theory]
    [InlineData("Hollowborn/HollowbornChaosEnvoy/0RunAll.cs")]
    [InlineData("Farm/REP/HollowbornREP.cs")]
    [InlineData("ScoreTracker.cs")]
    [InlineData("Farm/Encore.cs")]
    [InlineData("CoreFoo/Bar.cs")]
    public void Bots_are_listed_only_a_name_prefix_Core_or_a_folder_named_Core_excludes(string relative)
    {
        Assert.Single(Catalog([relative]).Scan());
    }

    [Fact]
    public void The_bot_file_check_gets_the_full_path_and_false_excludes()
    {
        var asked = new List<string>();
        var catalog = new SkuaScriptCatalog(Root, _ => new[] { At("Farm/Bot.cs"), At("Enhancement/ItemEnhancementManager.cs"), At("CoreBots.cs") },
            _ => false, path => { asked.Add(path); return !path.EndsWith("ItemEnhancementManager.cs"); });

        var scripts = catalog.Scan();

        Assert.Equal(["Bot"], scripts.Select(s => s.Name));
        Assert.Contains(At("Enhancement/ItemEnhancementManager.cs"), asked);
        Assert.DoesNotContain(At("CoreBots.cs"), asked);
        Assert.False(catalog.TryResolve(SkuaScriptCatalog.IdOf("Enhancement/ItemEnhancementManager.cs"), out _));
    }

    [Fact]
    public void A_throwing_bot_file_check_lists_the_script()
    {
        var catalog = new SkuaScriptCatalog(Root, _ => new[] { At("Farm/Bot.cs") }, _ => false, _ => throw new IOException("locked"));

        Assert.Equal(["Bot"], catalog.Scan().Select(s => s.Name));
    }

    [Fact]
    public void The_cap_counts_only_listed_scripts()
    {
        var files = Enumerable.Range(0, SkuaProtocol.MaxScripts).Select(i => $"Core/c{i:D5}.cs")
            .Concat(Enumerable.Range(0, SkuaProtocol.MaxScripts).Select(i => $"s{i:D5}.cs")).ToList();
        var catalog = Catalog(files);

        Assert.Equal(SkuaProtocol.MaxScripts, catalog.Scan().Count);
        Assert.False(catalog.Truncated);
    }
}
