using AqwCompanion.App.Account;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Wiki;

namespace AqwCompanion.App.Shell;

public sealed class MainViewModel
{
    public MainViewModel(AccountViewModel account, WikiViewModel wiki, DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms)
    {
        Account = account;
        Wiki = wiki;
        Diagnostics = diagnostics;
        HardFarms = hardFarms;

        // The Hard Farms data is kept with the account's saved data, so it goes when that is removed.
        account.DataForgotten += (_, _) => hardFarms.Reset();
    }

    public string Title { get; } = AppInfo.ProductName;
    public AccountViewModel Account { get; }
    public WikiViewModel Wiki { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public HardFarmsViewModel HardFarms { get; }
}
