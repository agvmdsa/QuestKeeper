using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeEquipmentLoader(Func<string, Task<EquipmentLoadResult>> load) : ICharacterEquipmentLoader
{
    public List<string> Requested { get; } = [];

    public Task<EquipmentLoadResult> LoadAsync(string characterName, CancellationToken ct = default)
    {
        Requested.Add(characterName);
        return load(characterName);
    }
}

public sealed class FakeBadgesLoader(Func<int, Task<BadgesLoadResult>> load) : ICharacterBadgesLoader
{
    public List<int> Requested { get; } = [];

    public Task<BadgesLoadResult> LoadAsync(int characterId, CancellationToken ct = default)
    {
        Requested.Add(characterId);
        return load(characterId);
    }
}
