using System.Reflection;
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

    /// <summary>
    /// Removes lone surrogates (<see cref="WithoutLoneSurrogates"/>), then cuts the text to <paramref name="max"/> chars
    /// without splitting a pair.
    /// </summary>
    public static string Cut(string value, int max = SkuaProtocol.MaxTextLength)
    {
        value = WithoutLoneSurrogates(value);
        if (value.Length <= max)
        {
            return value;
        }

        var length = max;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    /// <summary>
    /// The message of an error as the plugin reports it: the inner error of a reflection call (Skua invokes scripts by
    /// reflection), every absolute path reduced to its file name (<see cref="WithFileNamesOnly"/>, so the Windows user name in
    /// a local folder does not leave the plugin), trimmed and cut to <see cref="SkuaProtocol.MaxTextLength"/>; the error's
    /// type name when it has no message.
    /// </summary>
    public static string ErrorText(Exception exception)
    {
        var actual = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        string? message;
        try
        {
            message = actual.Message;
        }
        catch (Exception)
        {
            message = null;
        }

        var text = string.IsNullOrWhiteSpace(message) ? "" : WithFileNamesOnly(message).Trim();
        return Cut(text.Length > 0 ? text : actual.GetType().Name);
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
