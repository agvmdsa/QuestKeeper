using AqwCompanion.Core.Capture;

namespace AqwCompanion.App.GameData;

/// <summary>Where the captured Characters are kept between runs.</summary>
public interface IGameDataStore
{
    /// <summary>The stored Characters, validated; empty on any failure.</summary>
    IReadOnlyList<CharacterSnapshot> Load();

    /// <summary>Replaces the stored list with <paramref name="characters"/>. Never throws.</summary>
    void Save(IReadOnlyList<CharacterSnapshot> characters);
}
