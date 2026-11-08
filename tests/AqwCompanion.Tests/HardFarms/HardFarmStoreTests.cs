using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmStoreTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly InMemoryStoredDataStore _inner = new();

    private HardFarmStore Build() => new(_inner.Module());

    [Fact]
    public void Nothing_is_stored_at_first()
    {
        var store = Build();

        Assert.Null(store.LoadList());
        Assert.Null(store.LoadTypes());
        Assert.Empty(store.LoadIndexes());
    }

    [Fact]
    public void The_list_round_trips_with_its_read_time_under_its_own_key()
    {
        var store = Build();
        var list = new HardFarmList([new HardFarmGroup("Nation", ["a", "b"]), new HardFarmGroup("Legion", ["b", "c"])], "Created by Tester");

        store.SaveList(list, At);
        var loaded = store.LoadList()!;

        Assert.True(_inner.Items.ContainsKey("hardfarms.list"));
        Assert.Equal(At, loaded.ReadAtUtc);
        Assert.Equal("Created by Tester", loaded.List.Credit);
        Assert.Equal(["Nation", "Legion"], loaded.List.Groups.Select(g => g.Name));
        Assert.Equal(["a", "b", "c"], loaded.List.Entries.Select(e => e.Slug));
        Assert.Equal(3, loaded.List.Count);
    }

    private static WikiIndex Index(string slug, WikiType type, params (string Slug, string Name)[] entries) =>
        new(slug, type, entries.Select(e => new WikiIndexEntry(e.Slug, e.Name)).ToList());

    [Fact]
    public void An_index_round_trips_with_its_own_read_time_under_the_indexes_key()
    {
        var store = Build();

        store.SaveIndex(Index("classes", WikiType.Class, ("a", "Alpha"), ("b", "Beta")), At);
        var loaded = Assert.Single(store.LoadIndexes());

        Assert.True(_inner.Items.ContainsKey("hardfarms.indexes"));
        Assert.Equal(At, loaded.ReadAtUtc);
        Assert.Equal("classes", loaded.Index.IndexSlug);
        Assert.Equal(WikiType.Class, loaded.Index.Type);
        Assert.Equal([new WikiIndexEntry("a", "Alpha"), new WikiIndexEntry("b", "Beta")], loaded.Index.Entries);
    }

    [Fact]
    public void Each_index_keeps_its_own_read_time_and_saving_one_again_replaces_only_that_one()
    {
        var store = Build();
        store.SaveIndex(Index("classes", WikiType.Class, ("a", "Alpha")), At);
        store.SaveIndex(Index("pets", WikiType.Pet, ("b", "Beta")), At.AddDays(1));
        store.SaveIndex(Index("classes", WikiType.Class, ("a2", "Alpha Two")), At.AddDays(2));

        var loaded = store.LoadIndexes().ToDictionary(i => i.Index.IndexSlug);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(At.AddDays(2), loaded["classes"].ReadAtUtc);
        Assert.Equal(["a2"], loaded["classes"].Index.Entries.Select(e => e.Slug));
        Assert.Equal(At.AddDays(1), loaded["pets"].ReadAtUtc);
    }

    [Fact]
    public void The_type_table_is_rebuilt_from_all_stored_indexes_with_the_priority_rule()
    {
        var store = Build();
        store.SaveIndex(Index("misc-items", WikiType.Other, ("x", "X Misc"), ("m", "Mine")), At);
        store.SaveIndex(Index("armors", WikiType.Armor, ("x", "X Armor")), At.AddHours(2));
        store.SaveIndex(Index("pets", WikiType.Pet, ("x", "X Pet")), At.AddHours(1));

        var types = store.LoadTypes()!;

        Assert.Equal(new WikiTypeRow("x", "X Armor", WikiType.Armor), types.Table.Find("x"));
        Assert.Equal(new WikiTypeRow("m", "Mine", WikiType.Other), types.Table.Find("m"));
        Assert.Equal(2, types.Table.Count);
        Assert.Equal(At.AddHours(2), types.ReadAtUtc);
    }

    [Fact]
    public void The_old_single_type_table_is_ignored_on_load_and_removed_on_the_first_save()
    {
        _inner.Items["hardfarms.types"] = ("legacy type table", At);
        var store = Build();

        Assert.Empty(store.LoadIndexes());
        Assert.Null(store.LoadTypes());
        Assert.True(_inner.Items.ContainsKey("hardfarms.types"));

        store.SaveIndex(Index("classes", WikiType.Class, ("b", "Beta")), At);

        Assert.False(_inner.Items.ContainsKey("hardfarms.types"));
        Assert.Equal(["b"], store.LoadTypes()!.Table.Rows.Select(r => r.Slug));
    }

    [Fact]
    public void The_old_single_type_table_is_also_removed_after_the_first_list_save()
    {
        _inner.Items["hardfarms.types"] = ("legacy type table", At);
        var store = Build();

        store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["a"])], null), At);

        Assert.False(_inner.Items.ContainsKey("hardfarms.types"));
        Assert.True(_inner.Items.ContainsKey("hardfarms.list"));
    }

    [Fact]
    public void A_failed_save_leaves_the_old_single_type_table_in_place()
    {
        _inner.Items["hardfarms.types"] = ("legacy type table", At);
        _inner.ThrowOnSave = true;

        Build().SaveList(new HardFarmList([], null), At);

        Assert.True(_inner.Items.ContainsKey("hardfarms.types"));
    }

    [Fact]
    public void Store_failures_are_reported_by_operation_and_exception_type_only()
    {
        var failures = new List<(string Operation, string Type)>();
        _inner.ThrowOnSave = true;
        var store = new HardFarmStore(_inner.Module((operation, type) => failures.Add((operation, type))));

        store.SaveList(new HardFarmList([], null), At);

        Assert.Equal([("Save", "IOException")], failures);
    }

    [Fact]
    public void Store_failures_are_swallowed()
    {
        _inner.ThrowOnSave = true;
        _inner.ThrowOnLoad = true;
        var store = Build();

        store.SaveList(new HardFarmList([], null), At);
        store.SaveIndex(Index("classes", WikiType.Class, ("a", "Alpha")), At);

        Assert.Null(store.LoadList());
        Assert.Null(store.LoadTypes());
        Assert.Empty(store.LoadIndexes());
    }

    private void Stored(params HardFarmStore.IndexDto[] indexes) =>
        _inner.Items["hardfarms.indexes"] = (new HardFarmStore.IndexesDto(indexes.ToList()), At);

    private static HardFarmStore.IndexDto Dto(string slug, string type, params string[] entrySlugs) =>
        new(slug, type, entrySlugs.Select(e => new HardFarmStore.EntryDto(e, e.ToUpperInvariant())).ToList(), At);

    [Fact]
    public void Indexes_with_an_unknown_type_name_or_slug_are_dropped_on_load()
    {
        Stored(Dto("classes", "Nonsense", "a"), Dto("armors", "Armor", "b"), Dto("not-an-index", "Armor", "c"));

        var loaded = Build().LoadIndexes();

        Assert.Equal(["armors"], loaded.Select(i => i.Index.IndexSlug));
    }

    [Fact]
    public void The_type_table_keeps_the_highest_priority_type_for_a_slug()
    {
        var table = new WikiTypeTable([
            new WikiTypeRow("x", "X Misc", WikiType.Other),
            new WikiTypeRow("x", "X Armor", WikiType.Armor),
            new WikiTypeRow("x", "X Pet", WikiType.Pet)]);

        Assert.Equal(new WikiTypeRow("x", "X Armor", WikiType.Armor), table.Find("x"));
        Assert.Equal(1, table.Count);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Class,Armor")]
    [InlineData("class")]
    [InlineData(" Class")]
    [InlineData("Nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public void Indexes_whose_type_is_not_exactly_a_defined_name_are_dropped_on_load(string? type)
    {
        Stored(Dto("classes", type!, "a"), Dto("armors", "Armor", "b"));

        var loaded = Build().LoadIndexes();

        Assert.Equal(["armors"], loaded.Select(i => i.Index.IndexSlug));
    }

    [Fact]
    public void Slugs_that_are_not_valid_page_names_are_dropped_on_load()
    {
        _inner.Items["hardfarms.list"] = (new HardFarmStore.ListDto([new("G", ["ok-one", "a?b", "a b", "A", "", "x/y", "ok_two"])], null), At);
        Stored(Dto("classes", "Class", "a#b", "good", "Bad", "also-good"));

        var store = Build();

        Assert.Equal(["ok-one", "ok_two"], store.LoadList()!.List.Groups[0].Slugs);
        Assert.Equal(["good", "also-good"], store.LoadTypes()!.Table.Rows.Select(r => r.Slug));
    }

    [Fact]
    public void Duplicate_indexes_and_a_null_entry_list_do_not_break_loading()
    {
        Stored(Dto("classes", "Class", "a"), Dto("classes", "Class", "b"), new HardFarmStore.IndexDto("armors", "Armor", null!, At));

        var loaded = Build().LoadIndexes();

        Assert.Equal(["classes", "armors"], loaded.Select(i => i.Index.IndexSlug));
        Assert.Equal(["b"], loaded[0].Index.Entries.Select(e => e.Slug));
        Assert.Empty(loaded[1].Index.Entries);
    }

    [Fact]
    public void The_stored_dtos_serialize_with_the_stored_json_options()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new HardFarmStore.ListDto([new HardFarmStore.GroupDto("N", ["a"])], "c"),
            AqwCompanion.App.Infrastructure.Persistence.StoredJson.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<HardFarmStore.ListDto>(
            json, AqwCompanion.App.Infrastructure.Persistence.StoredJson.Options)!;

        Assert.Equal("a", back.Groups.Single().Slugs.Single());
        Assert.Equal("c", back.Credit);
    }

    [Fact]
    public void The_indexes_dto_round_trips_through_the_stored_json_options()
    {
        var options = AqwCompanion.App.Infrastructure.Persistence.StoredJson.Options;
        var dto = new HardFarmStore.IndexesDto([new("classes", "Class", [new("a", "Alpha")], At)]);

        var back = System.Text.Json.JsonSerializer.Deserialize<HardFarmStore.IndexesDto>(
            System.Text.Json.JsonSerializer.Serialize(dto, options), options)!;

        var index = Assert.Single(back.Indexes);
        Assert.Equal("classes", index.IndexSlug);
        Assert.Equal("Class", index.Type);
        Assert.Equal(At, index.ReadAtUtc);
        Assert.Equal("Alpha", index.Entries.Single().Name);
    }
}
