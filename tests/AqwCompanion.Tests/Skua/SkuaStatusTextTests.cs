using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaStatusTextTests
{
    private static SkuaView View(SkuaConnection c, SkuaHello? hello = null) => new(c, hello, null, []);

    [Fact]
    public void Not_connected_explains_how_to_install_the_plugin()
    {
        Assert.Equal("Skua is not connected. Copy the plugin into Skua's plugins folder and reopen Skua.", SkuaStatusText.For(View(SkuaConnection.NotConnected)));
    }

    [Fact]
    public void Connected_without_hello_is_connecting()
    {
        Assert.Equal("Skua is connecting...", SkuaStatusText.For(View(SkuaConnection.Connected)));
    }

    [Fact]
    public void Connected_with_the_tested_version_has_no_warning()
    {
        Assert.Equal("Skua connected (plugin 0.1.0, Skua 1.4.5.0).", SkuaStatusText.For(View(SkuaConnection.Connected, new SkuaHello("0.1.0", "1.4.5.0"))));
    }

    [Fact]
    public void Connected_with_another_skua_version_says_what_was_tested()
    {
        Assert.Equal(
            "Skua connected (plugin 0.1.0, Skua 1.5.0.0). Tested with Skua 1.4.5.0.",
            SkuaStatusText.For(View(SkuaConnection.Connected, new SkuaHello("0.1.0", "1.5.0.0"))));
    }

    [Fact]
    public void A_version_mismatch_tells_the_user_to_use_the_matching_plugin()
    {
        Assert.Equal(
            "The plugin and Questkeeper do not speak the same version. Use the plugin from the same release as this app.",
            SkuaStatusText.For(View(SkuaConnection.VersionMismatch)));
    }
}
