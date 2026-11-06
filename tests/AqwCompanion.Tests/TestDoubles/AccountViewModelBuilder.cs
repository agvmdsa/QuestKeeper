using AqwCompanion.App.Account;
using AqwCompanion.App.Badges;
using AqwCompanion.App.Equipment;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.SignIn;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Builds the coordinator with real card and sidebar instances; reach them through <c>vm.Summary</c> and friends.</summary>
public static class AccountViewModelBuilder
{
    public static AccountViewModel Create(
        ISignInDialog dialog,
        ICharacterEquipmentLoader? equipmentLoader = null,
        ICharacterBadgesLoader? badgesLoader = null,
        IAccountDataPersistence? persistence = null,
        TimeProvider? clock = null,
        IAccountSessionData? session = null,
        ICharacterInventoryLoader? inventoryLoader = null)
    {
        var sidebar = new SidebarViewModel();
        return new AccountViewModel(
            dialog,
            new SummaryCardViewModel(),
            new EquipmentCardViewModel(sidebar),
            new BadgesCardViewModel(sidebar),
            new InventoryCardViewModel(sidebar),
            sidebar,
            equipmentLoader,
            badgesLoader,
            inventoryLoader,
            persistence,
            clock,
            session);
    }
}
