namespace AqwCompanion.App.Infrastructure.AutoStart;

/// <summary>Starting the app when the player signs in to the PC.</summary>
public interface IAutoStart
{
    /// <summary>False where the app cannot register itself (not Windows, or its own path is unknown); the switch is then not offered.</summary>
    bool IsSupported { get; }

    /// <summary>Whether the app is registered to start at sign-in. Never throws.</summary>
    bool IsEnabled();

    /// <summary>Registers or removes the start at sign-in; false when it did not work. Never throws.</summary>
    bool TrySet(bool enabled);
}

/// <summary>Runs a program and returns its exit code, or -1 when it could not run. Never throws.</summary>
public interface IProcessRunner
{
    int Run(string file, IReadOnlyList<string> args);
}
