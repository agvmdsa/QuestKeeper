using AqwCompanion.App.Account;
using AqwCompanion.Core.Equipment;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Equipment;

/// <summary>The Equipped card: a short preview on the dashboard and the full list in the details sidebar.</summary>
public partial class EquipmentCardViewModel(SidebarViewModel sidebar) : ObservableObject
{
    public const string LoadingText = "Loading equipment...";
    public const string UnavailableText = "Could not load the character page. Try Refresh again.";
    public const string NotFoundText = "No equipment found on the character page.";
    public const string SidebarTitle = "Equipped";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(PreviewSlots))]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailsCommand))]
    private CharacterEquipment? _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText), nameof(HasUpdatedText))]
    private DateTimeOffset? _readAt;

    public bool HasData => Data is not null;

    public bool HasStatus => Status.Length > 0;

    /// <summary>The first few standard slots, shown on the dashboard card.</summary>
    public IEnumerable<EquipmentSlot> PreviewSlots => Data?.StandardSlots.Take(5) ?? [];

    public string UpdatedText => UpdatedTextFormatter.Format(ReadAt);

    public bool HasUpdatedText => ReadAt is not null;

    /// <summary>Opens the equipped details sidebar; only possible while there is equipment to show.</summary>
    [RelayCommand(CanExecute = nameof(HasData))]
    private void OpenDetails() => sidebar.Open(SidebarTitle, this);

    partial void OnDataChanged(CharacterEquipment? value)
    {
        if (value is null)
        {
            sidebar.CloseIf(this);
        }
    }

    public void Clear()
    {
        Data = null;
        ReadAt = null;
        Status = string.Empty;
        sidebar.CloseIf(this);
    }
}
