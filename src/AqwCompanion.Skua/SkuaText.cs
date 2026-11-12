using System.Text;
using System.Text.RegularExpressions;

namespace AqwCompanion.Skua;

/// <summary>Pure text rules for what the plugin sends (the plugin compiles this library in as source, so they are tested without Skua).</summary>
public static partial class SkuaText
{
    /// <summary>
    /// Removes every lone surrogate and keeps valid pairs. <c>Utf8JsonWriter</c> throws on a lone surrogate, and the parser
    /// refuses a string holding U+FFFD, so the char is removed rather than replaced: the rest of the text still arrives.
    /// </summary>
    public static string WithoutLoneSurrogates(string value)
    {
        var first = FirstLoneSurrogate(value);
        if (first < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        builder.Append(value, 0, first);
        for (var i = first + 1; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(value[i]).Append(value[i + 1]);
                i++;
            }
            else if (!char.IsSurrogate(value[i]))
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reduces every absolute path in the text to its file name, so a message such as an exception's cannot carry a local
    /// folder (which holds the Windows user name). Covers drive paths (<c>C:\</c>, <c>C:/</c>), UNC paths and POSIX paths;
    /// a quoted path is taken whole up to its closing quote, spaces included; an unquoted Windows path keeps folders with
    /// spaces as long as a separator follows them.
    /// </summary>
    public static string WithFileNamesOnly(string value)
    {
        var quoted = QuotedPath().Replace(value, m => m.Groups["q"].Value + FileName(m.Groups["p"].Value) + m.Groups["q"].Value);
        return UnquotedPath().Replace(quoted, m => FileName(m.Value));
    }

    private static string FileName(string path)
    {
        var parts = path.Split('\\', '/');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].Length > 0)
            {
                return parts[i];
            }
        }

        return string.Empty;
    }

    private static int FirstLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex("""(?<q>['"])(?<p>(?:[A-Za-z]:[\\/]|\\\\|/(?=[^\s/'"]))[^'"\r\n]*)\k<q>""")]
    private static partial Regex QuotedPath();

    [GeneratedRegex("""(?<![\w])[A-Za-z]:[\\/](?:[^\\/\r\n'"<>|]*[\\/])*[^\s'"<>|\\/]*|(?<![\w\\])\\\\[^\\/\s]+[\\/](?:[^\\/\r\n'"<>|]*[\\/])*[^\s'"<>|\\/]*|(?<![\w/\\.:~-])/(?:[^\s/'"<>|]+/)+[^\s'"<>|/]*""")]
    private static partial Regex UnquotedPath();
}
