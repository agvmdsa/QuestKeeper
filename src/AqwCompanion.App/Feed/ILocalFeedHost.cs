namespace AqwCompanion.App.Feed;

/// <summary>The thing that actually listens for the local feed. Behind an interface so the controller is tested without a port.</summary>
public interface ILocalFeedHost
{
    bool IsRunning { get; }

    /// <summary>Starts listening; false when the address could not be used (the port may be taken or denied). Never throws.</summary>
    bool TryStart();

    /// <summary>Stops listening and ends every open event stream. Safe to call when not running.</summary>
    void Stop();
}
