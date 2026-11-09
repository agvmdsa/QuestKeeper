using System.Security.Cryptography;
using System.Text;

namespace AqwCompanion.App.Feed;

/// <summary>
/// The secret the local feed asks for: 32 random bytes written as base64url text to <c>feed.token</c> in the data folder, which only the
/// Windows user (and administrators) can read. Programs that may read the feed read the file. The text is kept in memory once read.
/// Never throws for a missing or damaged file: it makes a new secret.
/// </summary>
public sealed class FeedSecretStore(string path)
{
    private const int Bytes = 32;

    private readonly object _lock = new();
    private string? _token;

    public string Path { get; } = path;

    /// <summary>The secret as the text programs send in <c>Authorization: Bearer</c>; made on first use.</summary>
    public string GetOrCreate()
    {
        lock (_lock)
        {
            return _token ??= Read() ?? Write();
        }
    }

    /// <summary>Replaces the secret; programs that used the old one are refused from now on.</summary>
    public string Regenerate()
    {
        lock (_lock)
        {
            return _token = Write();
        }
    }

    /// <summary>The secret's bytes as the feed compares them (the UTF-8 of the text).</summary>
    public byte[] GetOrCreateBytes() => Encoding.UTF8.GetBytes(GetOrCreate());

    private string? Read()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            var text = File.ReadAllText(Path).Trim();
            return IsValid(text) ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string Write()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(Bytes));
        var temp = Path + ".tmp";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(temp, token);
        File.Move(temp, Path, overwrite: true);
        return token;
    }

    private static bool IsValid(string text)
    {
        if (text.Length != 43)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
