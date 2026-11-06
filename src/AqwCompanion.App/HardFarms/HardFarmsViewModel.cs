using System.ComponentModel;
using System.Globalization;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.App.Inventory;
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

/// <summary>The Hard Farms tab: the imported list with item types and what the character already owns.</summary>
public partial class HardFarmsViewModel : ObservableObject
{
    public const string EmptyText = "Click 'Update from the Wiki' to import the Hard Farming list (about 15 pages, ~45 s).";
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
    private readonly IHardFarmUpdater _updater;
    private readonly InventoryCardViewModel _inventory;
    private readonly IUrlLauncher _launcher;

    private HardFarmList? _list;
    private WikiTypeTable _types = WikiTypeTable.Empty;
    private DateTimeOffset? _listReadAt;
    private IReadOnlyList<Candidate> _candidates = [];
    private CancellationTokenSource? _updateCts;

    // Bumped by Reset: work that started before it must not bring data back.
    private int _epoch;

    // Bumped whenever data is applied: a restore that started earlier must not overwrite it.
    private int _dataVersion;

    // One entry of the full list with everything that does not depend on the filters or the inventory computed once.
    private sealed record Candidate(string Slug, string Name, WikiType Type, string Group, string WikiUrl);

    private IReadOnlyList<HardFarmRow> _all = [];

    public HardFarmsViewModel(
        IHardFarmStore store,
        IHardFarmUpdater updater,
        InventoryCardViewModel inventory,
        IUrlLauncher launcher)
    {
        _store = store;
        _updater = updater;
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
    [NotifyPropertyChangedFor(nameof(IsUpdating), nameof(IsIdle), nameof(ShowEmptyState))]
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

    public string EmptyStateText => EmptyText;

    private bool CanUpdate() => State == HardFarmsState.Idle;

    private bool CanCancel() => State == HardFarmsState.Updating;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        using var cts = new CancellationTokenSource();
        _updateCts = cts;
        var epoch = _epoch;
        State = HardFarmsState.Updating;
        StatusText = string.Empty;
        ProgressText = "Starting...";
        HardFarmUpdateResult result;
        try
        {
            result = await _updater.UpdateAsync(
                p =>
                {
                    if (epoch == _epoch)
                    {
                        ProgressText = p.Text;
                    }
                },
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new HardFarmUpdateResult(HardFarmUpdateStatus.Cancelled, "Update cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("The Hard Farms update failed unexpectedly: {ExceptionType}", ex.GetType().Name);
            result = new HardFarmUpdateResult(HardFarmUpdateStatus.Failed, "The update failed unexpectedly. Nothing was changed.");
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
            // Reset ran meanwhile: the data was forgotten, so nothing of this run is shown or kept in view.
            return;
        }

        if (result.List is not null || result.Types is not null)
        {
            Apply(result.List ?? _list, result.Types ?? _types, result.List is not null ? result.ReadAtUtc : _listReadAt);
        }

        ProgressText = string.Empty;
        StatusText = result.Message;
        State = HardFarmsState.Idle;
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
    /// Loads the stored list and type table in the background. Does nothing after <see cref="Reset"/> or when a list is already
    /// shown; when an update brought newer types meanwhile, those are kept.
    /// </summary>
    public async Task RestoreAsync()
    {
        var epoch = _epoch;
        var version = _dataVersion;
        var (list, types) = await Task.Run(() => (_store.LoadList(), _store.LoadTypes()));
        if (list is null || _list is not null || epoch != _epoch)
        {
            return;
        }

        var newerTypes = version != _dataVersion;
        Apply(list.List, newerTypes ? _types : types?.Table ?? WikiTypeTable.Empty, list.ReadAtUtc);
    }

    /// <summary>
    /// The account's saved data was removed: cancels a running update, forgets the list, the types and the filters and returns
    /// to the empty state. Answers of work that started before this call are ignored.
    /// </summary>
    public void Reset()
    {
        _epoch++;
        _dataVersion++;
        _updateCts?.Cancel();
        _updateCts = null;
        _list = null;
        _types = WikiTypeTable.Empty;
        _listReadAt = null;
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
                // An entry the type table does not know has no wiki name to compare.
                OwnershipMatcher.Match(_types.Find(c.Slug)?.Name, inventory),
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

        if (owned is null && needle.Length == 0)
        {
            Rows = scope;
        }
        else
        {
            Rows = scope
                .Where(row => (owned is null || row.Ownership == owned)
                    && (needle.Length == 0 || row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();
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
