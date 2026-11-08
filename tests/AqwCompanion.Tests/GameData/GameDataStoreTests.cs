using AqwCompanion.App.GameData;
using AqwCompanion.Core.Capture;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.GameData;

public class GameDataStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private const string Key = "gamedata.characters";

    private static CharacterProfile Profile(string name = "Fictional Hero") =>
        new(name, 60, 123456, 654321, 9876543210L, 777, 5000, 300, 90, 80, 70, 5, true, 25, 10, 20, 30, 40);

    private static CharacterSnapshot Snapshot(string name = "Fictional Hero", int items = 2) =>
        new(
            Profile(name),
            [new FactionStanding(5, "Fictional Order", 3, 5000, 6400, 1400), new FactionStanding(6, "Other Order", 10, 302500, 0, 0)],
            [
                new OwnedItem("Fictional Blade", "Sword", 1, 1, 50, ItemFlags.Coins | ItemFlags.Upgrade | ItemFlags.Equipped | ItemFlags.Worn
                    | ItemFlags.Quest | ItemFlags.Bank | ItemFlags.House | ItemFlags.Temp, 11, null, 5511),
                new OwnedItem("Fictional Class", "Class", 1, 1, 1, ItemFlags.None, 12, 9, 5512),
                .. Enumerable.Range(0, Math.Max(0, items - 2)).Select(i => new OwnedItem("Filler " + i, "Item", 1, 1, 1, ItemFlags.None, 100 + i, null)),
            ],
            [new OwnedItem("Fictional Chair", "House Item", 2, 5, 0, ItemFlags.House, 13, null)],
            At);

    private static (GameDataStore Store, InMemoryStoredDataStore Inner) New()
    {
        var inner = new InMemoryStoredDataStore();
        return (new GameDataStore(inner.Module()), inner);
    }

    private static GameDataStore.ProfileDto ProfileDto(string name = "Fictional Hero", int level = 60) =>
        new(name, level, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, true, 1, 1, 1, 1, 1);

    private static GameDataStore.CharacterDto CharacterDto(
        string name = "Fictional Hero", int level = 60, List<GameDataStore.ItemDto>? items = null, List<GameDataStore.FactionDto>? factions = null) =>
        new(ProfileDto(name, level), factions ?? [], items ?? [], [], At);

    private static (GameDataStore Store, InMemoryStoredDataStore Inner) WithDto(params GameDataStore.CharacterDto[] characters)
    {
        var inner = new InMemoryStoredDataStore();
        inner.Items[Key] = (new GameDataStore.GameDataDto([.. characters]), At);
        return (new GameDataStore(inner.Module()), inner);
    }

    [Fact]
    public void A_full_snapshot_is_loaded_back_unchanged()
    {
        var (store, _) = New();
        var original = Snapshot();

        store.Save([original]);

        var loaded = Assert.Single(store.Load());
        Assert.Equal(original.Profile, loaded.Profile);
        Assert.Equal(original.Factions, loaded.Factions);
        Assert.Equal(original.Items, loaded.Items);
        Assert.Equal(original.HouseItems, loaded.HouseItems);
        Assert.Equal(At, loaded.CapturedAtUtc);
        Assert.Equal(ItemFlags.Coins | ItemFlags.Upgrade | ItemFlags.Equipped | ItemFlags.Worn | ItemFlags.Quest | ItemFlags.Bank | ItemFlags.House | ItemFlags.Temp, loaded.Items[0].Flags);
        Assert.Equal(9, loaded.Items[1].ClassId);
        Assert.Equal([5511, 5512], loaded.Items.Take(2).Select(i => i.CharItemId));
    }

    [Fact]
    public void Old_stored_items_without_a_char_item_id_load_with_zero()
    {
        var json = """{"name":"Old Blade","type":"Sword","quantity":1,"stack":1,"level":5,"flags":0,"itemId":11,"classId":null}""";

        var dto = System.Text.Json.JsonSerializer.Deserialize<GameDataStore.ItemDto>(
            json, AqwCompanion.App.Infrastructure.Persistence.StoredJson.Options)!;
        var (store, _) = WithDto(CharacterDto(items: [dto]));

        Assert.Equal(0, dto.CharItemId);
        Assert.Equal(0, Assert.Single(Assert.Single(store.Load()).Items).CharItemId);
    }

    [Fact]
    public void A_negative_char_item_id_drops_the_item()
    {
        var items = new List<GameDataStore.ItemDto> { new("Bad", "Item", 1, 1, 1, 0, 1, null, -3), new("Good", "Item", 1, 1, 1, 0, 2, null, 77) };
        var (store, _) = WithDto(CharacterDto(items: items));

        var item = Assert.Single(Assert.Single(store.Load()).Items);

        Assert.Equal(("Good", 77), (item.Name, item.CharItemId));
    }

    [Fact]
    public void An_empty_store_loads_as_an_empty_list() =>
        Assert.Empty(New().Store.Load());

    [Fact]
    public void The_data_lives_under_the_game_data_key()
    {
        var (store, inner) = New();

        store.Save([Snapshot()]);

        Assert.Equal([Key], inner.Items.Keys);
    }

    [Fact]
    public void Save_replaces_the_previous_list()
    {
        var (store, _) = New();
        store.Save([Snapshot("First"), Snapshot("Second")]);

        store.Save([Snapshot("Third")]);

        Assert.Equal(["Third"], store.Load().Select(c => c.Profile.Name));
    }

    [Fact]
    public void Saving_an_empty_list_removes_the_record()
    {
        var (store, inner) = New();
        store.Save([Snapshot()]);

        store.Save([]);

        Assert.Empty(inner.Items);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void A_failing_store_never_throws()
    {
        var inner = new InMemoryStoredDataStore { ThrowOnSave = true };
        var store = new GameDataStore(inner.Module());

        store.Save([Snapshot()]);

        inner.ThrowOnSave = false;
        store.Save([Snapshot()]);
        inner.ThrowOnLoad = true;
        Assert.Empty(store.Load());
    }

    [Fact]
    public void A_record_stored_under_another_version_is_not_loaded()
    {
        var (store, inner) = New();
        store.Save([Snapshot()]);
        inner.Versions[Key] = 2;

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Characters_with_negative_numbers_or_empty_names_are_dropped()
    {
        var (store, _) = WithDto(CharacterDto(level: -1), CharacterDto(name: " "), CharacterDto(name: "Kept"));

        Assert.Equal(["Kept"], store.Load().Select(c => c.Profile.Name));
    }

    [Fact]
    public void Duplicate_character_names_keep_the_first_ignoring_case()
    {
        var (store, _) = WithDto(CharacterDto(name: "Hero", level: 10), CharacterDto(name: "HERO", level: 20), CharacterDto(name: "Other"));

        var loaded = store.Load();

        Assert.Equal(["Hero", "Other"], loaded.Select(c => c.Profile.Name));
        Assert.Equal(10, loaded[0].Profile.Level);
    }

    [Fact]
    public void More_than_sixteen_characters_are_truncated()
    {
        var (store, _) = WithDto([.. Enumerable.Range(0, 20).Select(i => CharacterDto(name: "Hero " + i))]);

        var loaded = store.Load();

        Assert.Equal(16, loaded.Count);
        Assert.Equal("Hero 0", loaded[0].Profile.Name);
    }

    [Fact]
    public void More_than_five_thousand_items_are_truncated()
    {
        var items = Enumerable.Range(0, 5200).Select(i => new GameDataStore.ItemDto("Item " + i, "Item", 1, 1, 1, 0, i, null)).ToList();
        var (store, _) = WithDto(CharacterDto(items: items));

        Assert.Equal(5000, Assert.Single(store.Load()).Items.Count);
    }

    [Fact]
    public void Invalid_items_and_factions_are_dropped_and_unknown_flag_bits_are_removed()
    {
        var items = new List<GameDataStore.ItemDto>
        {
            new("", "Item", 1, 1, 1, 0, 1, null),
            new("Negative", "Item", -1, 1, 1, 0, 2, null),
            new("Bad class", "Item", 1, 1, 1, 0, 3, -4),
            new("Good", "Item", 1, 1, 1, 4 | 1024 | 65536, 4, null),
        };
        var factions = new List<GameDataStore.FactionDto>
        {
            new(1, "", 1, 1, 1, 1),
            new(2, "Negative", -1, 1, 1, 1),
            new(3, "Good", 1, 1, 1, 1),
            new(3, "Duplicate", 2, 2, 2, 2),
        };
        var (store, _) = WithDto(CharacterDto(items: items, factions: factions));

        var loaded = Assert.Single(store.Load());

        var item = Assert.Single(loaded.Items);
        Assert.Equal(("Good", ItemFlags.Equipped), (item.Name, item.Flags));
        Assert.Equal("Good", Assert.Single(loaded.Factions).Name);
    }

    [Fact]
    public void The_stored_shape_is_made_of_plain_records_without_core_types()
    {
        var coreAssembly = typeof(CharacterSnapshot).Assembly;
        var dtos = typeof(GameDataStore).GetNestedTypes().ToList();

        Assert.Equal(5, dtos.Count);
        foreach (var dto in dtos)
        {
            foreach (var property in dto.GetProperties().Where(p => p.Name != "EqualityContract"))
            {
                var types = new[] { property.PropertyType }.Concat(property.PropertyType.GenericTypeArguments);
                Assert.All(types, t => Assert.NotEqual(coreAssembly, t.Assembly));
            }
        }
    }
}
