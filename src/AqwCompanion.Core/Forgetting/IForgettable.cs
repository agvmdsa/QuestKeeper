namespace AqwCompanion.Core.Forgetting;

/// <summary>A feature that holds character data in memory, in side files, or as running work.</summary>
public interface IForgettable
{
    /// <summary>The name shown when this feature could not be forgotten.</summary>
    string Name { get; }

    /// <summary>Stops in-flight work and makes late answers and writes have no effect. Reversible with <see cref="Resume"/>. Must not remove data.</summary>
    void Quiesce();

    /// <summary>Undoes <see cref="Quiesce"/>; called only when the stored-data removal failed.</summary>
    void Resume();

    /// <summary>Clears memory and side files. Called only after the stored data was removed.</summary>
    void Forget();
}
