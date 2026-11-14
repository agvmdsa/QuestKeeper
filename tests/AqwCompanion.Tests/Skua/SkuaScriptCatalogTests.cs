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
    public void The_facts_hook_fills_title_description_and_tags_and_gets_the_full_path_once_per_file()
    {
        var asked = new List<string>();
        var catalog = new SkuaScriptCatalog(Root, _ => new[] { At("Farm/Bot.cs"), At("Plain.cs"), At("CoreBots.cs") }, _ => false, null,
            path =>
            {
                asked.Add(path);
                return path.EndsWith("Bot.cs") ? new SkuaScriptFacts(true, "A title", "A description", "a, b") : null;
            });

        var scripts = catalog.Scan();

        Assert.Equal(new SkuaScriptInfo(SkuaScriptCatalog.IdOf("Farm/Bot.cs"), "Bot", "Farm", "A title", "A description", "a, b"), scripts.Single(s => s.Name == "Bot"));
        Assert.Equal(new SkuaScriptInfo(SkuaScriptCatalog.IdOf("Plain.cs"), "Plain", ""), scripts.Single(s => s.Name == "Plain"));
        Assert.Equal(2, asked.Count);
        Assert.DoesNotContain(At("CoreBots.cs"), asked);
    }

    [Fact]
    public void A_facts_verdict_of_not_a_bot_excludes_the_script()
    {
        var catalog = new SkuaScriptCatalog(Root, _ => new[] { At("Lib.cs"), At("Bot.cs") }, _ => false, null,
            path => new SkuaScriptFacts(path.EndsWith("Bot.cs"), "T", null, null));

        Assert.Equal(["Bot"], catalog.Scan().Select(s => s.Name));
        Assert.False(catalog.TryResolve(SkuaScriptCatalog.IdOf("Lib.cs"), out _));
    }

    [Fact]
    public void A_throwing_facts_hook_lists_the_script_without_header_data()
    {
        var catalog = new SkuaScriptCatalog(Root, _ => new[] { At("Bot.cs") }, _ => false, null, _ => throw new IOException("locked"));

        var only = Assert.Single(catalog.Scan());
        Assert.Equal(("Bot", null, null, null), (only.Name, only.Title, only.Description, only.Tags));
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

    // The facts budget: a fake clock moves on only when a file is read; the fake cache stands for the plugin's.
    private sealed class Reading
    {
        public long Now;
        public long CostMs = 3000;
        public readonly Dictionary<string, SkuaScriptFacts> Cache = new();
        public readonly List<string> Reads = [];
        public readonly object Gate = new();
        public Func<string, bool> Throws = _ => false;

        public SkuaScriptFacts? Read(string path)
        {
            lock (Gate)
            {
                Reads.Add(path);
                Interlocked.Add(ref Now, CostMs);
                if (Throws(path))
                {
                    throw new IOException("locked");
                }

                var facts = new SkuaScriptFacts(!path.EndsWith("Lib.cs"), "T " + Path.GetFileNameWithoutExtension(path), null, null);
                Cache[path] = facts;
                return facts;
            }
        }

        public SkuaScriptFacts? Peek(string path)
        {
            lock (Gate)
            {
                return Cache.GetValueOrDefault(path);
            }
        }

        public SkuaScriptCatalog Catalog(IEnumerable<string> relative, Action<IReadOnlySet<string>>? retain = null, int budgetMs = 8000)
        {
            var files = relative.Select(At).ToList();
            return new SkuaScriptCatalog(Root, _ => files, _ => false, null, Read,
                new SkuaScriptFactsOptions { Cached = Peek, Retain = retain, Budget = TimeSpan.FromMilliseconds(budgetMs), ClockMs = () => Interlocked.Read(ref Now) });
        }
    }

    private static readonly string[] Five = ["a.cs", "b.cs", "c.cs", "d.cs", "Lib.cs"];

    [Fact]
    public void When_the_budget_is_spent_the_rest_is_listed_without_header_data_and_read_in_the_background()
    {
        var reading = new Reading();
        using var catalog = reading.Catalog(Five);

        var first = catalog.Scan();

        Assert.Equal(["a", "b", "c", "d", "Lib"], first.Select(s => s.Name));
        Assert.Equal(["T a", "T b", "T c"], first.Take(3).Select(s => s.Title));
        Assert.Null(first[3].Title);
        Assert.Contains(first, s => s.Name == "Lib"); // unread: listed, the ScriptMain check was not made
        Assert.True(catalog.WaitForBackground(TimeSpan.FromSeconds(10)));

        var second = catalog.Scan();

        Assert.Equal(["a", "b", "c", "d"], second.Select(s => s.Name));
        Assert.Equal(["T a", "T b", "T c", "T d"], second.Select(s => s.Title));
        Assert.Equal(5, reading.Reads.Count); // nothing read twice
    }

    [Fact]
    public void A_scan_inside_the_budget_reads_everything_and_starts_no_background_work()
    {
        var reading = new Reading { CostMs = 1 };
        using var catalog = reading.Catalog(Five);

        Assert.Equal(["a", "b", "c", "d"], catalog.Scan().Select(s => s.Name));
        Assert.True(catalog.WaitForBackground(TimeSpan.Zero));
        Assert.Equal(5, reading.Reads.Count);
    }

    [Fact]
    public void Files_already_cached_cost_no_budget_and_come_with_their_data()
    {
        var reading = new Reading();
        using var catalog = reading.Catalog(Five);
        catalog.Scan();
        Assert.True(catalog.WaitForBackground(TimeSpan.FromSeconds(10)));
        reading.Now = 0;
        reading.Reads.Clear();

        var again = catalog.Scan();

        Assert.Empty(reading.Reads);
        Assert.All(again, s => Assert.NotNull(s.Title));
    }

    [Fact]
    public void A_throwing_hook_in_the_background_does_not_stop_the_other_files()
    {
        var reading = new Reading { Throws = p => p.EndsWith("d.cs") };
        using var catalog = reading.Catalog(Five);
        catalog.Scan();

        Assert.True(catalog.WaitForBackground(TimeSpan.FromSeconds(10)));

        Assert.Contains(At("Lib.cs"), reading.Reads);
        Assert.Equal(["a", "b", "c", "d"], catalog.Scan().Select(s => s.Name)); // d: listed without data; Lib: now known as a library
    }

    [Fact]
    public void A_newer_scan_replaces_the_pending_files_and_only_one_worker_runs()
    {
        var reading = new Reading();
        using var catalog = reading.Catalog(Five);

        catalog.Scan();
        catalog.Scan();
        catalog.Scan();

        Assert.True(catalog.WaitForBackground(TimeSpan.FromSeconds(10)));
        Assert.Equal(5, reading.Reads.Distinct().Count());
    }

    [Fact]
    public void Disposing_stops_the_background_work_and_never_throws()
    {
        var reading = new Reading();
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var files = Five.Concat(Enumerable.Range(0, 50).Select(i => $"x{i}.cs")).Select(At).ToList();
        var catalog = new SkuaScriptCatalog(Root, _ => files, _ => false, null,
            path =>
            {
                if (reading.Reads.Count >= 5)
                {
                    started.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }

                return reading.Read(path);
            },
            new SkuaScriptFactsOptions { Cached = reading.Peek, ClockMs = () => Interlocked.Read(ref reading.Now) });
        catalog.Scan();
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));

        catalog.Dispose();
        release.Set();

        Assert.True(catalog.WaitForBackground(TimeSpan.FromSeconds(10)));
        Assert.True(reading.Reads.Count < files.Count);
        catalog.Dispose();
        Assert.NotEmpty(catalog.Scan()); // still usable, but no new background work
        var readsAfter = reading.Reads.Count;
        Assert.True(catalog.WaitForBackground(TimeSpan.Zero));
        Thread.Sleep(50);
        Assert.Equal(readsAfter, reading.Reads.Count);
    }

    [Fact]
    public void The_retain_hook_gets_the_paths_of_the_scan_so_the_cache_can_be_pruned()
    {
        var reading = new Reading { CostMs = 1 };
        IReadOnlySet<string>? kept = null;
        using var catalog = reading.Catalog(["a.cs", "Core/x.cs", "b.cs"], retain: set => kept = set);

        catalog.Scan();

        Assert.NotNull(kept);
        Assert.Equal([At("a.cs"), At("b.cs")], kept!.Order());
    }

    [Fact]
    public void A_throwing_retain_hook_is_ignored()
    {
        var reading = new Reading { CostMs = 1 };
        using var catalog = reading.Catalog(["a.cs"], retain: _ => throw new InvalidOperationException());

        Assert.Single(catalog.Scan());
    }
}
