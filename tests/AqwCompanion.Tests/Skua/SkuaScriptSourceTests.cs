using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaScriptSourceTests
{
    [Theory]
    [InlineData("public void ScriptMain(IScriptInterface Bot)\n{\n}")]
    [InlineData("public class X { public void  ScriptMain  (IScriptInterface Bot) { } }")]
    [InlineData("void\r\n\tScriptMain(IScriptInterface Bot) {}")]
    [InlineData("// header\npublic void ScriptMain(IScriptInterface Bot) {}")]
    [InlineData("string s = \"//\"; public void ScriptMain(IScriptInterface Bot) {}")]
    [InlineData("/* a */ public void ScriptMain(IScriptInterface Bot) {} // tail")]
    public void A_script_main_declaration_counts(string text) => Assert.True(SkuaScriptSource.DeclaresScriptMain(text));

    [Theory]
    [InlineData("")]
    [InlineData("public class CoreBots { public void Other() {} }")]
    [InlineData("public void MyScriptMain(IScriptInterface Bot) {}")]
    [InlineData("// public void ScriptMain(IScriptInterface Bot) {}")]
    [InlineData("/* public void ScriptMain(IScriptInterface Bot) {} */ class X {}")]
    [InlineData("/* a\n public void ScriptMain(\n b */ class X {}")]
    [InlineData("Bot.ScriptMain(x);")]
    public void No_declaration_does_not_count(string text) => Assert.False(SkuaScriptSource.DeclaresScriptMain(text));

    [Fact]
    public void Null_does_not_count() => Assert.False(SkuaScriptSource.DeclaresScriptMain(null!));
}
