namespace AqwCompanion.Skua;

/// <summary>
/// The plugin's side of the two-way pipe: reads the lines SkuaLink sends through <see cref="BoundedLineReader"/> with the
/// <see cref="SkuaProtocol.MaxCommandLineBytes"/> cap and hands every well-formed command to a handler. A longer line, an
/// unknown <c>type</c>, a line of another version, anything that is not a command and anything malformed is dropped.
/// Never throws: it returns when the stream ends, fails or is cancelled, and a handler that throws does not stop it.
/// </summary>
public static class SkuaCommandReader
{
    public static async Task ReadAsync(Stream stream, Action<SkuaCommand> onCommand, CancellationToken ct)
    {
        try
        {
            await foreach (var line in BoundedLineReader.ReadAsync(stream, ct, SkuaProtocol.MaxCommandLineBytes).ConfigureAwait(false))
            {
                if (line.TooLong || line.Text is not { } text || SkuaLine.Parse(text) is not SkuaCommand command)
                {
                    continue;
                }

                try
                {
                    onCommand(command);
                }
                catch (Exception)
                {
                    // One command that cannot be handed over must not stop the next ones.
                }
            }
        }
        catch (Exception)
        {
            // A closed, broken or cancelled pipe ends the reading; the caller reconnects.
        }
    }
}
