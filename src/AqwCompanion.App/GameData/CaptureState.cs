namespace AqwCompanion.App.GameData;

/// <summary>Where the Capture session is.</summary>
public enum CaptureState
{
    Idle,
    NotSupported,
    NpcapMissing,
    NeedsAdministrator,
    Listening,
    Done,
    Failed,
}
