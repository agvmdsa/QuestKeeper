using System.ComponentModel;
using System.Globalization;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.App.Inventory;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Wiki;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.HardFarms;

public enum HardFarmsState
{
    Idle,
    Updating,
}

/// <summary>
/// The Hard Farms tab: the imported list with item types and what the character already owns. The sync reports from the
/// thread pool, so the app passes <c>onUiThread</c> to run what it reports on the UI thread (without it, inline).
/// </summary>
public partial class HardFarmsViewModel : ObservableObject, IForgettable
{
    public const string EmptyText = "Click 'Update from the Wiki' to import the Hard Farming list. The list shows up in a few seconds; the item types keep arriving for a minute or two.";
    public const string UpdateText = "Update from the Wiki";
    public const string ContinueText = "Continue";
    public const string UpdateAccessibleName = "Update the Hard Farming list from the Wiki";
    public const string ContinueAccessibleName = "Continue reading the pending Wiki pages";
    public const string NoMatchesText = "No entries match the filters.";
    public const string NoInventoryHint = "refresh your account to see what you own";
    public const string LicenseText = "AQW Wiki (CC BY-SA 3.0)";

    public static readonly FilterOption<WikiType> AllTypes = new(null, "All types");
    public static readonly GroupOption AllGroups = new(null, "All groups");
    public static readonly FilterOption<Ownership> AllOwnership = new(null, "All");

    private static readonly IReadOnlyList<FilterOption<WikiType>> TypeChoices =
        [AllTypes, .. Enum.GetValues<WikiType>().Select(t => new FilterOption<WikiType>(t, t.ToString()))];

    private static readonly IReadOnlyList<FilterOption<Ownership>> OwnedChoices =
        [AllOwnership, new(Ownership.Missing, "Missing"), new(Ownership.Owned, "Owned"), new(Ownership.Unknown, "Unknown")];

    private readonly IHardFarmStore _store;
    private readonly IHardFarmSync _sync;
    private readonly InventoryCardViewModel _inventory;
    private readonly IUrlLauncher _launcher;
    private readonly Action<Action> _onUiThread;

    private HardFarmList? _list;
    private WikiTypeTable _types = WikiTypeTable.Empty;
    private DateTimeOffset? _listReadAt;
    private IReadOnlyList<Candidate> _candidates = [];
    private CancellationTokenSource? _updateCts;
    private IReadOnlyList<string> _pending = [];
    private bool _everRead;

    // Bumped by Quiesce and Forget: work that started before it must not bring data back.
    private int _epoch;

    // Set by Quiesce: the update command does nothing until Resume or Forget.
    private bool _quiesced;

    // Bumped whenever data is applied: a restore that started earlier must not overwrite it.
    private int _dataVersion;

    // One entry of the full list with everything that does not depend on the filters or the inventory computed once.
    private sealed record Candidate(string Slug, string Name, WikiType Type, string Group, string WikiUrl);

    private IReadOnlyList<HardFarmRow> _all = [];

    public HardFarmsViewModel(
        IHardFarmStore store,
        IHardFarmSync sync,
        InventoryCardViewModel inventory,
        IUrlLauncher launcher,
        Action<Action>? onUiThread = null)
    {
        _onUiThread = onUiThread ?? (action => action());
        _store = store;
        _sync = sync;
        _inventory = inventory;
        _launcher = launcher;
        _typeFilter = AllTypes;
        _groupFilter = AllGroups;
        _ownedFilter = AllOwnership;
        inventory.PropertyChanged += OnInventoryChanged;
    }

    public IReadOnlyList<FilterOption<WikiType>> TypeOptions => TypeChoices;

    public IReadOnlyList<FilterOption<Ownership>> OwnedOptions => OwnedChoices;

    [ObservableProperty]
    private IReadOnlyList<GroupOption> _groupOptions = [AllGroups];

    [ObservableProperty]
    private FilterOption<WikiType> _typeFilter;

    [ObservableProperty]
    private GroupOption _groupFilter;

    [ObservableProperty]
    private FilterOption<Ownership> _ownedFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<HardFarmRow> _rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdating), nameof(IsIdle), nameof(ShowEmptyState), nameof(ShowPending))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(CancelCommand))]
    private HardFarmsState _state = HardFarmsState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    [ObservableProperty]
    private string _creditText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowNoMatches))]
    private bool _hasList;

    public bool IsUpdating => State == HardFarmsState.Updating;

    public bool IsIdle => State == HardFarmsState.Idle;

    public bool HasProgress => ProgressText.Length > 0;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>True until a list exists and no update is running; the view shows <see cref="EmptyText"/> then.</summary>
    public bool ShowEmptyState => !HasList && !IsUpdating;

    /// <summary>True when a list exists but the filters hide all of it.</summary>
    public bool ShowNoMatches => HasList && Rows.Count == 0;

    public bool HasCredit => CreditText.Length > 0;

    /// <summary>True when something was read before and index pages are still missing or old.</summary>
    public bool CanContinue => _everRead && _pending.Count > 0;

    public bool ShowPending => CanContinue && IsIdle;

    public string UpdateButtonText => CanContinue ? ContinueText : UpdateText;

    public string UpdateButtonAccessibleName => CanContinue ? ContinueAccessibleName : UpdateAccessibleName;

    public string PendingText => string.Create(
        CultureInfo.InvariantCulture,
        $"{_pending.Count} {(_pending.Count == 1 ? "page" : "pages")} pending — {ContinueText}");

    public string EmptyStateText => EmptyText;

    private void NotifyPending()
    {
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ShowPending));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(UpdateButtonAccessibleName));
        OnPropertyChanged(nameof(PendingText));
    }

    private bool CanUpdate() => State == HardFarmsState.Idle && !_quiesced;

    private bool CanCancel() => State == HardFarmsState.Updating;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        if (_quiesced)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _updateCts = cts;
        var epoch = _epoch;
        State = HardFarmsState.Updating;
        StatusText = string.Empty;
        ProgressText = "Starting...";
        SyncResult? result = null;
        string? failure = null;
        try
        {
            result = await _sync.RunAsync(
                p => _onUiThread(() =>
                {
                    if (epoch == _epoch)
                    {
                        ProgressText = ProgressLine(p);
                    }
                }),
                u => _onUiThread(() =>
                {
                    if (epoch == _epoch)
                    {
                        ApplyUpdate(u);
                    }
                }),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            failure = "Update cancelled.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("The Hard Farms update failed unexpectedly: {ExceptionType}", ex.GetType().Name);
            failure = "The update failed unexpectedly.";
        }
        finally
        {
            if (ReferenceEquals(_updateCts, cts))
            {
                _updateCts = null;
            }
        }

        if (epoch != _epoch)
        {
            // Forgetting ran meanwhile: the data was forgotten, so nothing of this run is shown or kept in view.
            return;
        }

        if (result is not null)
        {
            _pending = result.Pending;
        }

        ProgressText = string.Empty;
        StatusText = result?.Message ?? failure ?? string.Empty;
        State = HardFarmsState.Idle;
        NotifyPending();
    }

    private static string ProgressLine(SyncProgress p) =>
        p.Waiting is { Length: > 0 } ? $"{p.Label} · {p.Waiting}" : p.Label;

    private void ApplyUpdate(SyncUpdate update)
    {
        _everRead = true;
        Apply(update.List ?? _list, update.Types, update.List is not null ? update.ListReadAtUtc : _listReadAt);
        NotifyPending();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _updateCts?.Cancel();

    [RelayCommand]
    private void OpenOnWiki(HardFarmRow? row)
    {
        if (row is null || !WikiUrls.IsSafePageUrl(row.WikiUrl))
        {
            return;
        }

        try
        {
            _launcher.Open(row.WikiUrl);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Opening a wiki page failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Loads the stored list and type table in the background. Does nothing after <see cref="Forget"/> or when a list is already
    /// shown; when an update brought newer types meanwhile, those are kept.
    /// </summary>
    public async Task RestoreAsync()
    {
        var epoch = _epoch;
        var version = _dataVersion;
        var (list, types, pending) = await Task.Run(() => (_store.LoadList(), _store.LoadTypes(), _sync.GetPending()));
        if ((list is null && types is null) || _list is not null || epoch != _epoch)
        {
            return;
        }

        var newerTypes = version != _dataVersion;
        Apply(list?.List, newerTypes ? _types : types?.Table ?? WikiTypeTable.Empty, list?.ReadAtUtc);
        if (!newerTypes)
        {
            _pending = pending;
            _everRead = true;
            NotifyPending();
        }
    }

    public string Name => "Hard Farms";

    /// <summary>
    /// Stops a running update and makes its late answers have no effect; the update command does nothing until <see cref="Resume"/>.
    /// Removes no data. Pressing Update again after <see cref="Resume"/> starts over.
    /// </summary>
    public void Quiesce()
    {
        _quiesced = true;
        _epoch++;
        _dataVersion++;
        _updateCts?.Cancel();
        _updateCts = null;
        ProgressText = string.Empty;
        State = HardFarmsState.Idle;
        UpdateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Undoes <see cref="Quiesce"/> when the stored data could not be removed.</summary>
    public void Resume()
    {
        _quiesced = false;
        UpdateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The account's saved data was removed: forgets the list, the types and the filters and returns to the empty state.
    /// Answers of work that started before this call are ignored.
    /// </summary>
    public void Forget()
    {
        _epoch++;
        _dataVersion++;
        _updateCts = null;
        _quiesced = false;
        _list = null;
        _types = WikiTypeTable.Empty;
        _listReadAt = null;
        _pending = [];
        _everRead = false;
        _candidates = [];
        _all = [];
        GroupOptions = [AllGroups];
        TypeFilter = AllTypes;
        GroupFilter = AllGroups;
        OwnedFilter = AllOwnership;
        SearchText = string.Empty;
        HasList = false;
        LastUpdatedText = string.Empty;
        CreditText = string.Empty;
        OnPropertyChanged(nameof(HasCredit));
        ProgressText = string.Empty;
        StatusText = string.Empty;
        State = HardFarmsState.Idle;
        Refilter();
        NotifyPending();
        UpdateCommand.NotifyCanExecuteChanged();
    }

    // A drop-down can write null back when its items are replaced; that means "All".
    partial void OnTypeFilterChanged(FilterOption<WikiType> value)
    {
        if (value is null)
        {
            TypeFilter = AllTypes;
            return;
        }

        Refilter();
    }

    partial void OnGroupFilterChanged(GroupOption value)
    {
        if (value is null)
        {
            GroupFilter = AllGroups;
            return;
        }

        Refilter();
    }

    partial void OnOwnedFilterChanged(FilterOption<Ownership> value)
    {
        if (value is null)
        {
            OwnedFilter = AllOwnership;
            return;
        }

        Refilter();
    }

    partial void OnSearchTextChanged(string value) => Refilter();

    private void OnInventoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InventoryCardViewModel.Data) && _list is not null)
        {
            RebuildRows();
            Refilter();
        }
    }

    private void Apply(HardFarmList? list, WikiTypeTable types, DateTimeOffset? readAt)
    {
        _dataVersion++;
        _list = list;
        _types = types;
        _listReadAt = readAt;
        HasList = list is not null && list.Count > 0;
        LastUpdatedText = UpdatedTextFormatter.Format(readAt);
        CreditText = list is null ? string.Empty : CreditLine(list.Credit);
        OnPropertyChanged(nameof(HasCredit));

        var groupNames = list?.Groups.Where(g => g.Slugs.Count > 0).Select(g => g.Name).Distinct(StringComparer.Ordinal).ToList() ?? [];
        var options = new List<GroupOption> { AllGroups };
        options.AddRange(groupNames.Select(name => new GroupOption(name, name)));
        var chosenGroup = GroupFilter?.Name;
        GroupOptions = options;
        GroupFilter = options.FirstOrDefault(o => o.Name is not null && o.Name == chosenGroup) ?? AllGroups;

        _candidates = list is null
            ? []
            : list.Entries.Where(entry => WikiUrls.IsValidSlug(entry.Slug)).Select(entry =>
            {
                var row = types.Find(entry.Slug);
                return new Candidate(entry.Slug, row?.Name ?? Prettify(entry.Slug), row?.Type ?? WikiType.Other, entry.Group, WikiUrls.Page(entry.Slug));
            }).ToList();
        RebuildRows();
        Refilter();
    }

    private void RebuildRows()
    {
        var inventory = _inventory.Data;
        _all = _candidates
            .Select(c => new HardFarmRow(
                c.Slug,
                c.Name,
                c.Type,
                c.Group,
                OwnershipMatcher.Match(c.Slug, _types.Find(c.Slug)?.Name, inventory),
                c.WikiUrl))
            .ToList();
    }

    private void Refilter()
    {
        var type = TypeFilter?.Value;
        var group = GroupFilter?.Name;
        var owned = OwnedFilter?.Value;
        var needle = (SearchText ?? string.Empty).Trim();

        var scope = new List<HardFarmRow>(_all.Count);
        var ownedInScope = 0;
        foreach (var row in _all)
        {
            if ((type is null || row.Type == type) && (group is null || row.Group == group))
            {
                scope.Add(row);
                if (row.Ownership == Ownership.Owned)
                {
                    ownedInScope++;
                }
            }
        }

        SummaryText = _list is null
            ? string.Empty
            : _inventory.Data is null
                ? string.Create(CultureInfo.InvariantCulture, $"{scope.Count} {(scope.Count == 1 ? "entry" : "entries")} — {NoInventoryHint}")
                : string.Create(CultureInfo.InvariantCulture, $"{ownedInScope} of {scope.Count} owned");

        var shown = owned is null && needle.Length == 0
            ? scope
            : scope
                .Where(row => (owned is null || row.Ownership == owned)
                    && (needle.Length == 0 || row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        // Identical rows keep the same list instance, so the list does not reset its scroll position.
        if (!Rows.SequenceEqual(shown))
        {
            Rows = shown;
        }

        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private static string CreditLine(string? credit)
    {
        const string prefix = "Created by ";
        var name = credit?.Trim() ?? string.Empty;
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[prefix.Length..].Trim();
        }

        return name.Length == 0 ? $"Hard Farming list · {LicenseText}" : $"List by {name} · {LicenseText}";
    }

    /// <summary>"void-highlord-class" becomes "Void Highlord Class".</summary>
    public static string Prettify(string slug)
    {
        var words = slug.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
