using Skua.Core.Interfaces;

namespace QuestKeeper.SkuaBridge;

public sealed class SkuaBridgePlugin : ISkuaPlugin
{
    public string Name => "QuestKeeper Bridge";
    public string Author => "QuestKeeper";
    public string Description => "Shows what Skua is doing in Questkeeper. Read only.";
    public List<IOption>? Options => null;

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
    }

    public void Unload()
    {
    }
}
