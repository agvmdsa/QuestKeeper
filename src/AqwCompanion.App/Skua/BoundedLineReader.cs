using System.Runtime.CompilerServices;
using System.Text;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Skua;

public readonly record struct LineResult(string? Text, bool TooLong);

public static class BoundedLineReader
{
    public static async IAsyncEnumerable<LineResult> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[4096];
        var line = new List<byte>(256);
        var overflow = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                yield break;
            }

            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b == (byte)'\n')
                {
                    if (overflow)
                    {
                        yield return new LineResult(null, true);
                    }
                    else
                    {
                        if (line.Count > 0 && line[^1] == (byte)'\r')
                        {
                            line.RemoveAt(line.Count - 1);
                        }

                        if (line.Count > 0)
                        {
                            yield return new LineResult(Encoding.UTF8.GetString([.. line]), false);
                        }
                    }

                    line.Clear();
                    overflow = false;
                }
                else if (!overflow)
                {
                    if (line.Count >= SkuaProtocol.MaxLineBytes)
                    {
                        overflow = true;
                        line.Clear();
                    }
                    else
                    {
                        line.Add(b);
                    }
                }
            }
        }
    }
}
