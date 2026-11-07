using AqwCompanion.App.Account;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Wiki;

namespace AqwCompanion.App.Shell;

public sealed class MainViewModel
{
    public MainViewModel(AccountViewModel account, WikiViewModel wiki, DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms,
        ReputationViewModel reputation)
    {
        Account = account;
        Wiki = wiki;
        Diagnostics = diagnostics;
        HardFarms = hardFarms;
        Reputation = reputation;

        // The Hard Farms and Reputation data are kept with the account's saved data, so they go when that is removed.
        account.DataForgotten += (_, _) => hardFarms.Reset();
        account.DataForgotten += (_, _) => reputation.Reset();
    }

    public string Title { get; } = AppInfo.ProductName;
    public AccountViewModel Account { get; }
    public WikiViewModel Wiki { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public HardFarmsViewModel HardFarms { get; }
    public ReputationViewModel Reputation { get; }
}
