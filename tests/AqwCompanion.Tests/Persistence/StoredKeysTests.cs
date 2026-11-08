using System.Reflection;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Persistence;

namespace AqwCompanion.Tests.Persistence;

public sealed class StoredKeysTests
{
    [Fact]
    public void Every_key_name_is_unique_case_insensitively()
    {
        var names = StoredKeys.All.Select(k => k.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_key_has_a_name_an_owner_and_a_version_of_at_least_one()
    {
        Assert.All(StoredKeys.All, key =>
        {
            Assert.False(string.IsNullOrWhiteSpace(key.Name));
            Assert.False(string.IsNullOrWhiteSpace(key.Owner));
            Assert.True(key.Version >= 1);
        });
    }

    [Fact]
    public void No_obsolete_key_is_also_a_current_name()
    {
        Assert.Empty(StoredKeys.Obsolete.Intersect(StoredKeys.All.Select(k => k.Name), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_old_hard_farm_type_table_is_obsolete()
    {
        Assert.Contains("hardfarms.types", StoredKeys.Obsolete);
    }

    [Fact]
    public void The_records_of_the_old_website_sign_in_are_obsolete_so_older_databases_lose_them()
    {
        Assert.All(["identity", "summary", "equipment", "badges", "inventory"], name => Assert.Contains(name, StoredKeys.Obsolete));
    }

    [Fact]
    public void All_contains_every_public_stored_key_field()
    {
        var declared = typeof(StoredKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(StoredKey<>))
            .Select(f => (IStoredKey)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(declared);
        Assert.Equal(declared.Count, StoredKeys.All.Count);
        Assert.All(declared, key => Assert.Contains(key, StoredKeys.All));
    }

    [Fact]
    public void The_key_names_are_the_ones_older_databases_already_hold()
    {
        Assert.Equal(
            ["reputation.data", "hardfarms.list", "hardfarms.indexes", "gamedata.characters"],
            StoredKeys.All.Select(k => k.Name));
    }
}
