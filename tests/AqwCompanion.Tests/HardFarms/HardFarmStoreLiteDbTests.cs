using AqwCompanion.Tests.TestDoubles;
using AqwCompanion.App.Account;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Wiki;

namespace AqwCompanion.Tests.HardFarms;

public sealed class HardFarmStoreLiteDbTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aqw-hardfarm-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset ReadAt = new(2026, 11, 6, 12, 30, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private HardFarmStore Build() => new(new LiteDbStoredDataStore(Path.Combine(_directory, "data", "companion.db")));

    [Fact]
    public void Nothing_is_loaded_before_anything_was_saved()
    {
        var store = Build();

        Assert.Null(store.LoadList());
        Assert.Null(store.LoadTypes());
    }

    [Fact]
    public void The_list_and_the_type_table_round_trip_through_the_real_database()
    {
        var list = new HardFarmList(
            [new HardFarmGroup("Nation", ["blade-one", "cape-one"]), new HardFarmGroup("Legion", ["pet-one"])],
            "Created by Tester");
        var weapons = new WikiIndex(WikiUrls.WeaponsSlug, WikiType.Weapon, [new WikiIndexEntry("blade-one", "Blade One")]);
        var pets = new WikiIndex(WikiUrls.PetsSlug, WikiType.Pet, [new WikiIndexEntry("pet-one", "Pet One")]);

        Build().SaveList(list, ReadAt);
        Build().SaveIndex(weapons, ReadAt);
        Build().SaveIndex(pets, ReadAt);

        var again = Build();
        var loadedList = again.LoadList();
        Assert.NotNull(loadedList);
        Assert.Equal(ReadAt, loadedList.ReadAtUtc);
        Assert.Equal("Created by Tester", loadedList.List.Credit);
        Assert.Equal(["Nation", "Legion"], loadedList.List.Groups.Select(g => g.Name));
        Assert.Equal(["blade-one", "cape-one"], loadedList.List.Groups[0].Slugs);
        var loadedTypes = again.LoadTypes();
        Assert.NotNull(loadedTypes);
        Assert.Equal(ReadAt, loadedTypes.ReadAtUtc);
        Assert.Equal(WikiTypeTable.From([weapons, pets]).Rows, loadedTypes.Table.Rows);
        Assert.Equal(2, again.LoadIndexes().Count);
    }

    [Fact]
    public void Forgetting_the_account_data_removes_the_hard_farm_data_too()
    {
        var path = Path.Combine(_directory, "data", "companion.db");
        var database = new LiteDbStoredDataStore(path);
        var store = new HardFarmStore(database);
        store.SaveList(new HardFarmList([new HardFarmGroup("Nation", ["blade-one"])], null), ReadAt);
        store.SaveIndex(new WikiIndex(WikiUrls.WeaponsSlug, WikiType.Weapon, [new WikiIndexEntry("blade-one", "Blade One")]), ReadAt);
        Assert.NotNull(store.LoadList());

        Assert.True(new AccountDataPersistence(database).Forget());

        Assert.Null(database.Load<HardFarmStore.ListDto>(HardFarmStore.ListKey, 1));
        Assert.Null(database.Load<HardFarmStore.IndexesDto>(HardFarmStore.IndexesKey, 1));
        Assert.Null(store.LoadList());
        Assert.Null(store.LoadTypes());
    }
}
