namespace AqwCompanion.Core.Wiki;

/// <summary>The closed set of item types, in priority order (the first wins when a page is in several indexes).</summary>
public enum WikiType
{
    Class,
    Armor,
    Weapon,
    Helm,
    Cape,
    Pet,
    House,
    Other,
}

public static class WikiTypes
{
    /// <summary>The type of the items listed on a type index page; Other for every index without its own type.</summary>
    public static WikiType ForIndex(string indexSlug) => indexSlug switch
    {
        WikiUrls.ClassesSlug => WikiType.Class,
        WikiUrls.ArmorsSlug => WikiType.Armor,
        WikiUrls.WeaponsSlug => WikiType.Weapon,
        WikiUrls.HelmetsSlug => WikiType.Helm,
        WikiUrls.CapesSlug => WikiType.Cape,
        WikiUrls.PetsSlug or WikiUrls.BattlePetsSlug => WikiType.Pet,
        WikiUrls.HousesSlug => WikiType.House,
        _ => WikiType.Other,
    };

    /// <summary>The type to keep when a page is found in two indexes: the one earlier in the enum wins.</summary>
    public static WikiType Prefer(WikiType a, WikiType b) => a <= b ? a : b;
}
