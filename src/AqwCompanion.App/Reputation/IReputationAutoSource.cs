namespace AqwCompanion.App.Reputation;

/// <summary>What the game client's reputation file looked like when it was opened: the open stream (the receiver disposes it), its size and its last write time.</summary>
public sealed record ReputationAutoFile(Stream Content, long Length, DateTimeOffset LastWriteTimeUtc);

/// <summary>Whether the game client's file was found, and if so the opened file.</summary>
public enum ReputationAutoStatus
{
    Missing,
    Found,
    Unreadable,
}

/// <summary>The outcome of opening the game client's file. <see cref="File"/> is set only when <see cref="Status"/> is <see cref="ReputationAutoStatus.Found"/>.</summary>
public sealed record ReputationAutoRead(ReputationAutoStatus Status, ReputationAutoFile? File = null);

/// <summary>The one fixed file a game client keeps writing from outside the app. It is only read on the user's request and never watched.</summary>
public interface IReputationAutoSource
{
    /// <summary>The full path where the file is expected.</summary>
    string Path { get; }

    /// <summary>Opens the file for reading without blocking the client that writes it. Never throws.</summary>
    ReputationAutoRead Open();

    /// <summary>Removes the file and its folder contents, best effort. Never throws.</summary>
    void Delete();
}
