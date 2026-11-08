namespace AqwCompanion.App.Infrastructure.Elevation;

/// <summary>Running with administrator rights, which the Capture needs to open network devices.</summary>
public interface IProcessElevation
{
    bool IsElevated { get; }

    /// <summary>
    /// Starts this program again with the administrator prompt, passing the data root. true when the new process
    /// started (the caller then exits); false when the prompt was declined or elevation is not available. Never throws.
    /// </summary>
    bool TryRelaunchElevated(string dataRoot);
}
