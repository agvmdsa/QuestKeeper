using System.Text;

namespace AqwCompanion.Skua;

/// <summary>
/// Turns the first bytes of a script file into <see cref="SkuaScriptFacts"/>. Pure; the plugin reads the file (bounded) and
/// asks here. The encoding is found by byte order mark (UTF-8, UTF-16 either way, UTF-32 either way), UTF-8 without one.
/// Whatever cannot be decided means "list it": text that decodes with NUL characters (UTF-16 without a mark), and text cut
/// at the read cap in which no <c>ScriptMain</c> was seen. A working bot is never hidden by a bad guess.
/// </summary>
public static class SkuaScriptFileText
{
    public static SkuaScriptFacts Facts(byte[] bytes, int count, bool truncated)
    {
        try
        {
            if (bytes is null || count < 0 || count > bytes.Length)
            {
                return new SkuaScriptFacts(true, null, null, null);
            }

            var text = Decode(bytes, count);
            var (title, description, tags) = SkuaScriptHeader.Parse(text);
            if (text.Contains('\0'))
            {
                return new SkuaScriptFacts(true, null, null, null);
            }

            var isBot = SkuaScriptSource.DeclaresScriptMain(text) || truncated;
            return new SkuaScriptFacts(isBot, title, description, tags);
        }
        catch (Exception)
        {
            return new SkuaScriptFacts(true, null, null, null);
        }
    }

    /// <summary>The text of the bytes, the byte order mark (if any) decided on and dropped.</summary>
    public static string Decode(byte[] bytes, int count)
    {
        var (encoding, skip) = Detect(bytes, count);
        return encoding.GetString(bytes, skip, count - skip);
    }

    private static (Encoding Encoding, int Skip) Detect(byte[] b, int n)
    {
        if (n >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)
        {
            return (new UTF32Encoding(false, false), 4);
        }

        if (n >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF)
        {
            return (new UTF32Encoding(true, false), 4);
        }

        if (n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            return (new UTF8Encoding(false), 3);
        }

        if (n >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            return (new UnicodeEncoding(false, false), 2);
        }

        if (n >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            return (new UnicodeEncoding(true, false), 2);
        }

        return (new UTF8Encoding(false), 0);
    }
}
