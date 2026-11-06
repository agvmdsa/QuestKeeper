using AqwCompanion.App.Account;
using AqwCompanion.Core.Badges;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Badges;

/// <summary>The Badges card: a short preview on the dashboard and all badges grouped by category in the details sidebar.</summary>
public partial class BadgesCardViewModel(SidebarViewModel sidebar) : ObservableObject
{
    public const string LoadingText = "Loading badges...";
    public const string UnavailableText = "Could not load the badges. Try Refresh again.";
    public const string NotFoundText = "No badges found for this character.";
    public const string SidebarTitle = "Badges";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(CountText), nameof(Preview), nameof(Groups))]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailsCommand))]
    private CharacterBadges? _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText), nameof(HasUpdatedText))]
    private DateTimeOffset? _readAt;

    /// <summary>True when there is at least one badge to show; an empty list is reported through the status text.</summary>
    public bool HasData => Data is { Count: > 0 };

    public bool HasStatus => Status.Length > 0;

    public string CountText => Data is { Count: 1 } ? "1 badge" : $"{Data?.Count ?? 0} badges";

    /// <summary>The first few badges, shown on the dashboard card.</summary>
    public IEnumerable<Badge> Preview => Data?.Preview ?? [];

    /// <summary>All badges grouped by category, shown in the sidebar.</summary>
    public IReadOnlyList<BadgeGroup> Groups => Data?.GroupedByCategory ?? [];

    public string UpdatedText => UpdatedTextFormatter.Format(ReadAt);

    public bool HasUpdatedText => ReadAt is not null;

    /// <summary>Opens the badges sidebar; only possible while there are badges to show.</summary>
    [RelayCommand(CanExecute = nameof(HasData))]
    private void OpenDetails() => sidebar.Open(SidebarTitle, this);

    partial void OnDataChanged(CharacterBadges? value)
    {
        if (value is not { Count: > 0 })
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
