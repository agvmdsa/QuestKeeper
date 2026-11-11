namespace AqwCompanion.Skua;

public static class SkuaProtocol
{
    public const int Version = 1;
    public const int MaxLineBytes = 16 * 1024;
    public const int MaxTextLength = 300;
    public const string PipeName = "QuestKeeper.SkuaBridge.v1";

    /// <summary>The Skua version the plugin was built and tested against.</summary>
    public const string TestedSkuaVersion = "1.4.5.0";
}
