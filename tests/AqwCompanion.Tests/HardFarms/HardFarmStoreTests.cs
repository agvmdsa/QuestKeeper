using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmStoreTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly InMemoryStoredDataStore _inner = new();

    private HardFarmStore Build() => new(_inner);

    [Fact]
    public void Nothing_is_stored_at_first()
    {
        var store = Build();

        Assert.Null(store.LoadList());
        Assert.Null(store.LoadTypes());
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

    [Fact]
    public void The_type_table_round_trips_with_its_read_time_under_its_own_key()
    {
        var store = Build();
        var table = new WikiTypeTable([new WikiTypeRow("a", "Alpha", WikiType.Class), new WikiTypeRow("b", "Beta", WikiType.Pet)]);

        store.SaveTypes(table, At);
        var loaded = store.LoadTypes()!;

        Assert.True(_inner.Items.ContainsKey("hardfarms.types"));
        Assert.Equal(At, loaded.ReadAtUtc);
        Assert.Equal(new WikiTypeRow("a", "Alpha", WikiType.Class), loaded.Table.Find("a"));
        Assert.Equal(new WikiTypeRow("b", "Beta", WikiType.Pet), loaded.Table.Find("b"));
        Assert.Equal(2, loaded.Table.Count);
    }

    [Fact]
    public void Store_failures_are_swallowed()
    {
        _inner.ThrowOnSave = true;
        _inner.ThrowOnLoad = true;
        var store = Build();

        store.SaveList(new HardFarmList([], null), At);
        store.SaveTypes(WikiTypeTable.Empty, At);

        Assert.Null(store.LoadList());
        Assert.Null(store.LoadTypes());
    }

    [Fact]
    public void Rows_with_an_unknown_type_name_are_dropped_on_load()
    {
        _inner.Items["hardfarms.types"] = (new HardFarmStore.TypesDto([new("a", "Alpha", "Nonsense"), new("b", "Beta", "Armor")]), At);

        var table = Build().LoadTypes()!.Table;

        Assert.Null(table.Find("a"));
        Assert.Equal(WikiType.Armor, table.Find("b")!.Type);
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
    public void Rows_whose_type_is_not_exactly_a_defined_name_are_dropped_on_load(string? type)
    {
        _inner.Items["hardfarms.types"] = (new HardFarmStore.TypesDto([new("a", "Alpha", type!), new("b", "Beta", "Class")]), At);

        var table = Build().LoadTypes()!.Table;

        Assert.Null(table.Find("a"));
        Assert.Equal(WikiType.Class, table.Find("b")!.Type);
    }

    [Fact]
    public void Slugs_that_are_not_valid_page_names_are_dropped_on_load()
    {
        _inner.Items["hardfarms.list"] = (new HardFarmStore.ListDto([new("G", ["ok-one", "a?b", "a b", "A", "", "x/y", "ok_two"])], null), At);
        _inner.Items["hardfarms.types"] = (new HardFarmStore.TypesDto([new("a#b", "Bad", "Class"), new("good", "Good", "Class")]), At);

        var store = Build();

        Assert.Equal(["ok-one", "ok_two"], store.LoadList()!.List.Groups[0].Slugs);
        Assert.Equal(["good"], store.LoadTypes()!.Table.Rows.Select(r => r.Slug));
    }
}

