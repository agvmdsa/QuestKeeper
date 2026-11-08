namespace AqwCompanion.App.GameData;

/// <summary>Why a Capture session failed.</summary>
public enum CaptureFailure
{
    None,

    /// <summary>The source ended at once and saw no game traffic, for example because no network device could be opened.</summary>
    NoDevice,

    /// <summary>The source stopped with an error.</summary>
    DriverError,
}
