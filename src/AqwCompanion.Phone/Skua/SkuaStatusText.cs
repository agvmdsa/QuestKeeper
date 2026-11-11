namespace AqwCompanion.Phone.Skua;

public static class SkuaStatusText
{
    public const string TestedWith = "1.4.5.0";
    private const string NotConnectedText = "Skua is not connected. Copy the plugin into Skua's plugins folder and reopen Skua.";
    private const string ConnectingText = "Skua is connecting...";
    private const string MismatchText = "The plugin and Questkeeper do not speak the same version. Use the plugin from the same release as this app.";

    public static string For(SkuaView view) => view.Connection switch
    {
        SkuaConnection.VersionMismatch => MismatchText,
        SkuaConnection.Connected when view.Hello is null => ConnectingText,
        SkuaConnection.Connected => Connected(view.Hello),
        _ => NotConnectedText,
    };

    private static string Connected(AqwCompanion.Skua.SkuaHello hello)
    {
        var text = $"Skua connected (plugin {hello.PluginVersion}, Skua {hello.SkuaVersion}).";
        return hello.SkuaVersion == TestedWith ? text : text + $" Tested with Skua {TestedWith}.";
    }
}
