using System.Globalization;
using AqwCompanion.App.Infrastructure.Formatting;
using AqwCompanion.Core.Account;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AqwCompanion.App.Account;

/// <summary>The account summary card: AdventureCoins, account type, membership and Gears of Doom as read from the account page.</summary>
public partial class SummaryCardViewModel : ObservableObject
{
    public const string UnavailableText = "Not available. Try Refresh.";
    public const string MissingValueText = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasData), nameof(AdventureCoinsText), nameof(AccountTypeText), nameof(MembershipText), nameof(GearsOfDoomText))]
    private AccountSummary? _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText), nameof(HasUpdatedText))]
    private DateTimeOffset? _readAt;

    public bool HasData => Data is not null;

    /// <summary>AdventureCoins with thousands separators, or a dash when the page did not carry it.</summary>
    public string AdventureCoinsText =>
        Data?.AdventureCoins is { } coins ? coins.ToString("N0", CultureInfo.InvariantCulture) : MissingValueText;

    public string AccountTypeText => Data?.AccountType ?? MissingValueText;

    /// <summary>The page's own membership line, date included.</summary>
    public string MembershipText => Data?.MembershipStatus ?? MissingValueText;

    public string GearsOfDoomText =>
        Data?.GearsOfDoom is { } gears ? gears.ToString(CultureInfo.InvariantCulture) : MissingValueText;

    public string UpdatedText => UpdatedTextFormatter.Format(ReadAt);

    public bool HasUpdatedText => ReadAt is not null;

    public void Clear()
    {
        Data = null;
        ReadAt = null;
    }
}
