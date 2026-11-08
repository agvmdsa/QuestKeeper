using AqwCompanion.App.GameData;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeGameDataStore : IGameDataStore
{
    public List<CharacterSnapshot> Stored { get; } = [];

    public List<IReadOnlyList<CharacterSnapshot>> Saved { get; } = [];

    public int LoadCalls { get; private set; }

    public IReadOnlyList<CharacterSnapshot> Load()
    {
        LoadCalls++;
        return [.. Stored];
    }

    public void Save(IReadOnlyList<CharacterSnapshot> characters) => Saved.Add([.. characters]);
}
