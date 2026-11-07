using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Core.Common;
using AqwCompanion.Core.Reputation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Reputation;

/// <summary>
/// The Reputation tab: the standings imported from a JSON file the user picks. Nothing is read except that file, a failed
/// import leaves the previous data as it was, and the data is kept with the account's saved data.
/// </summary>
public partial class ReputationViewModel(IReputationStore store, IReputationFilePicker picker, TimeProvider clock) : ObservableObject
{
    public const string EmptyText = "Import a reputation file (JSON, schema 1) to see your faction standings here.";
    public const string ImportText = "Import reputation file…";
    public const string ImportAccessibleName = "Import a reputation file";
    public const string OpenFailedMessage = "The file could not be opened." + NothingChangedSuffix;
    public const string ReadFailedMessage = "The file could not be read." + NothingChangedSuffix;
    public const string NothingChangedSuffix = " Nothing was changed.";

    private const int MaxFileNameLength = 200;

    private int _epoch;

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

    public bool IsEmpty => !HasData;

    public bool HasStatus => StatusText.Length > 0;

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
