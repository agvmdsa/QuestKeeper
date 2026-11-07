namespace AqwCompanion.App.Reputation;

/// <summary>A file the user picked, opened for reading. The receiver disposes the stream.</summary>
public sealed record PickedFile(Stream Content, string Name);

/// <summary>Lets the user choose the reputation file to import. The app only ever opens a file the user picked.</summary>
public interface IReputationFilePicker
{
    /// <summary>The chosen file opened read-only, or null when the user cancelled. Throws when the file picker or the file cannot be opened.</summary>
    Task<PickedFile?> PickAsync();
}
