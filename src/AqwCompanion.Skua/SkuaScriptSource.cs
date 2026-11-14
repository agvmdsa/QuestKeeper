using System.Text;
using System.Text.RegularExpressions;

namespace AqwCompanion.Skua;

/// <summary>
/// Tells a bot script from a library by its text: a bot defines <c>public void ScriptMain(IScriptInterface Bot)</c>.
/// Pure; the plugin reads the file (bounded) and asks here.
/// </summary>
public static class SkuaScriptSource
{
    private static readonly Regex Declaration =
        new(@"\bvoid\s+ScriptMain\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// True when the text declares <c>void ScriptMain(</c> outside comments. A small scan blanks <c>//</c> and
    /// <c>/* */</c> comments, skipping over string and character literals (verbatim ones too); raw and interpolated
    /// strings are not understood, so a <c>ScriptMain</c> declaration printed inside a string would still count.
    /// A timeout counts as true (list it).
    /// </summary>
    public static bool DeclaresScriptMain(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            return Declaration.IsMatch(WithoutComments(text));
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static string WithoutComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                sb.Append('\n');
            }
            else if (c == '/' && next == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
                sb.Append(' ');
            }
            else if (c == '"' || c == '\'')
            {
                var verbatim = c == '"' && i > 0 && (text[i - 1] == '@' || (i > 1 && text[i - 1] == '$' && text[i - 2] == '@') || (i > 1 && text[i - 1] == '@' && text[i - 2] == '$'));
                var start = i++;
                for (; i < text.Length; i++)
                {
                    if (verbatim)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"') { i++; continue; }
                            break;
                        }
                    }
                    else if (text[i] == '\\') { i++; }
                    else if (text[i] == c || text[i] == '\n') { break; }
                }

                sb.Append(text, start, Math.Min(i + 1, text.Length) - start);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
