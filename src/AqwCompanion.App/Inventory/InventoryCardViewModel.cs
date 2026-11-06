using System.Globalization;
using AqwCompanion.App.Account;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Core.Inventory;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Inventory;

/// <summary>The Inventory card: the biggest item types on the dashboard and a filterable list of all items in the details sidebar.</summary>
public partial class InventoryCardViewModel(SidebarViewModel sidebar) : ObservableObject
{
    public const string LoadingText = "Loading inventory...";
    public const string UnavailableText = "Could not load the inventory. Try Refresh again.";
    public const string NotFoundText = "No inventory found for this character.";
    public const string SidebarTitle = "Inventory";

    private IReadOnlyList<InventoryItem> _sorted = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(CountText), nameof(TypePreview), nameof(TotalText), nameof(HasNoMatches))]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailsCommand))]
    private CharacterInventory? _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText), nameof(HasUpdatedText))]
    private DateTimeOffset? _readAt;

    /// <summary>What the sidebar filter box holds: item names containing it (ignoring case) are listed.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    private IReadOnlyList<InventoryItem> _filteredItems = [];

    /// <summary>The items matching <see cref="FilterText"/>, sorted by type and then name; recalculated when either input changes.</summary>
    public IReadOnlyList<InventoryItem> FilteredItems
    {
        get => _filteredItems;
        private set
        {
            if (SetProperty(ref _filteredItems, value))
            {
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(HasNoMatches));
            }
        }
    }

    /// <summary>True when there is at least one item to show; an empty list is reported through the status text.</summary>
    public bool HasData => Data is { Count: > 0 };

    public bool HasStatus => Status.Length > 0;

    public string CountText => Plural.Count(Data?.Count ?? 0, "item", "items");

    /// <summary>The biggest few types as "Type · N", shown on the dashboard card.</summary>
    public IEnumerable<string> TypePreview =>
        (Data?.TypeCounts ?? [])
            .Take(CharacterInventory.PreviewTypeCount)
            .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Type} · {entry.Count}"));

    /// <summary>The total line of the sidebar: "N items", or "M of N items" while the filter narrows the list.</summary>
    public string TotalText =>
        FilteredItems.Count == _sorted.Count
            ? CountText
            : string.Create(CultureInfo.InvariantCulture, $"{FilteredItems.Count} of {_sorted.Count} items");

    /// <summary>True when items exist but the filter hides all of them.</summary>
    public bool HasNoMatches => _sorted.Count > 0 && FilteredItems.Count == 0;

    public string UpdatedText => UpdatedTextFormatter.Format(ReadAt);

    public bool HasUpdatedText => ReadAt is not null;

    /// <summary>Opens the inventory sidebar; only possible while there are items to show.</summary>
    [RelayCommand(CanExecute = nameof(HasData))]
    private void OpenDetails() => sidebar.Open(SidebarTitle, this);

    partial void OnDataChanged(CharacterInventory? value)
    {
        // The data is cleared when another character loads (and on sign out); the filter belongs to the old list.
        if (value is null)
        {
            FilterText = string.Empty;
        }

        _sorted = value is null
            ? []
            : value.Items
                .OrderBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        Refilter();

        if (value is not { Count: > 0 })
        {
            sidebar.CloseIf(this);
        }
    }

    partial void OnFilterTextChanged(string value) => Refilter();

    private void Refilter()
    {
        var needle = (FilterText ?? string.Empty).Trim();
        FilteredItems = needle.Length == 0
            ? _sorted
            : _sorted.Where(item => item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void Clear()
    {
        Data = null;
        ReadAt = null;
        Status = string.Empty;
        FilterText = string.Empty;
        sidebar.CloseIf(this);
    }
}
