namespace AqwCompanion.Core.Persistence;

/// <summary>A declared stored record without its value type: the name, the version of the stored shape and the owning feature.</summary>
public interface IStoredKey
{
    string Name { get; }

    int Version { get; }

    string Owner { get; }
}
