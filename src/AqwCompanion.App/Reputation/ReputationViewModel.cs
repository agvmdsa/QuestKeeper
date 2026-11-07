using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Reputation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Reputation;

/// <summary>
/// The Reputation tab: the standings imported from a JSON file the user picks, or read from the game client's file when the
/// user opens the tab or presses Refresh. Nothing is read except those files, nothing is watched, a failed read leaves the
/// previous data as it was, and the data is kept with the account's saved data.
/// </summary>
public partial class ReputationViewModel(IReputationStore store, IReputationFilePicker picker, IReputationAutoSource autoSource, TimeProvider clock)
    : ObservableObject
{
    public const string EmptyText = "Import a reputation file (JSON, schema 1) to see your faction standings here.";
    public const string ImportText = "Import reputation file…";
    public const string ImportAccessibleName = "Import a reputation file";
    public const string OpenFailedMessage = "The file could not be opened." + NothingChangedSuffix;
    public const string ReadFailedMessage = "The file could not be read." + NothingChangedSuffix;
    public const string NothingChangedSuffix = " Nothing was changed.";
    public const string RefreshText = "Refresh";
    public const string RefreshAccessibleName = "Refresh reputation from the game client";
    public const string AutoSourceName = "reputation.json (auto)";

    private const int MaxFileNameLength = 200;

    private int _epoch;
    private bool _refreshing;
    private (DateTimeOffset LastWriteTimeUtc, long Length)? _lastAutoRead;

    [ObservableProperty]
    private IReadOnlyList<ReputationRow> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _importedLine = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChecked))]
    private string _checkedLine = string.Empty;

    /// <summary>Where the game client's file is expected, to be shown so that it can be copied.</summary>
    public string ExpectedPath => autoSource.Path;

    public bool IsEmpty => !HasData;

    public bool HasStatus => StatusText.Length > 0;

    public bool HasChecked => CheckedLine.Length > 0;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>Loads the stored reputation in the background. Does nothing after <see cref="Reset"/> or when data is already shown.</summary>
    public async Task RestoreAsync()
    {
        var epoch = _epoch;
        var stored = await Task.Run(store.Load);
        if (stored is null || HasData || epoch != _epoch)
        {
            return;
        }

        Apply(stored);
    }

    /// <summary>The account's saved data was removed: forgets the standings and returns to the empty state. Answers of an import that started before this call are ignored.</summary>
    public void Reset()
    {
        _epoch++;
        Items = [];
        HasData = false;
        StatusText = string.Empty;
        ErrorText = string.Empty;
        ImportedLine = string.Empty;
        CheckedLine = string.Empty;
        _lastAutoRead = null;
        autoSource.Delete();
    }

    /// <summary>
    /// Reads the game client's file: a missing file changes nothing, a file that has not changed since the last read only updates
    /// the checked time, and a new file replaces the data when it is valid. Only one refresh runs at a time.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            await RefreshCoreAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        var epoch = _epoch;
        var read = await Task.Run(autoSource.Open);
        if (epoch != _epoch)
        {
            if (read.File is { } late)
            {
                await late.Content.DisposeAsync();
            }

            return;
        }

        CheckedLine = $"Checked {LocalTimeText.Format(clock.GetUtcNow())}";
        if (read.Status == ReputationAutoStatus.Missing)
        {
            ErrorText = string.Empty;
            StatusText = $"No file from the game client yet. Expected at {autoSource.Path}.";
            return;
        }

        if (read.File is not { } file)
        {
            ErrorText = ReadFailedMessage;
            return;
        }

        await using (file.Content)
        {
            if (_lastAutoRead == (file.LastWriteTimeUtc, file.Length))
            {
                return;
            }

            ReputationParseResult result;
            if (file.Length > ReputationParser.MaxBytes)
            {
                result = ReputationParseResult.Failure(ReputationParser.TooLargeError);
            }
            else
            {
                try
                {
                    result = await ReputationParser.ParseAsync(file.Content);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Error("Reading the reputation file failed: {ExceptionType}", ex.GetType().Name);
                    ErrorText = ReadFailedMessage;
                    return;
                }
            }

            if (epoch != _epoch)
            {
                return;
            }

            if (result.Snapshot is not { } snapshot)
            {
                ErrorText = result.Error + NothingChangedSuffix;
                return;
            }

            _lastAutoRead = (file.LastWriteTimeUtc, file.Length);
            var stored = new StoredReputation(snapshot, AutoSourceName, clock.GetUtcNow());
            store.Save(stored);
            Apply(stored);
            StatusText = result.SkippedCount > 0
                ? $"{snapshot.Factions.Count} factions loaded from the game client, {result.SkippedCount} entries skipped."
                : $"{snapshot.Factions.Count} factions loaded from the game client.";
        }
    }

    /// <summary>Asks for a file and imports it. A cancelled dialog changes nothing; an invalid file shows an error and keeps the previous data.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var epoch = _epoch;
        PickedFile? picked;
        try
        {
            picked = await picker.PickAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Picking the reputation file failed: {ExceptionType}", ex.GetType().Name);
            ErrorText = OpenFailedMessage;
            return;
        }

        if (picked is null)
        {
            return;
        }

        ReputationParseResult result;
        await using (picked.Content)
        {
            try
            {
                result = await ReputationParser.ParseAsync(picked.Content);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("Reading the reputation file failed: {ExceptionType}", ex.GetType().Name);
                ErrorText = ReadFailedMessage;
                return;
            }
        }

        if (epoch != _epoch)
        {
            return;
        }

        if (result.Snapshot is not { } snapshot)
        {
            ErrorText = result.Error + NothingChangedSuffix;
            return;
        }

        var name = TextCleaner.Clean(picked.Name, MaxFileNameLength);
        var stored = new StoredReputation(snapshot, name.Length == 0 ? "file" : name, clock.GetUtcNow());
        store.Save(stored);
        Apply(stored);
        StatusText = result.SkippedCount > 0
            ? $"{snapshot.Factions.Count} factions imported, {result.SkippedCount} entries skipped."
            : $"{snapshot.Factions.Count} factions imported.";
    }

    private void Apply(StoredReputation stored)
    {
        Items = stored.Snapshot.Factions
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FactionId)
            .Select(ReputationRow.From)
            .ToList();
        HasData = true;
        ErrorText = string.Empty;
        StatusText = string.Empty;
        ImportedLine = $"Imported {LocalTimeText.Format(stored.ImportedAt)} from {stored.SourceName}";
    }
}
