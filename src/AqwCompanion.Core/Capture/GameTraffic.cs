namespace AqwCompanion.Core.Capture;

/// <summary>Where the game's own traffic lives on the wire.</summary>
public static class GameTraffic
{
    public const int FirstPort = 5588;
    public const int LastPort = 5594;
    public const string CaptureFilter = "tcp and portrange 5588-5594";

    public static bool IsGamePort(int port) => port >= FirstPort && port <= LastPort;
}
