using AqwCompanion.Core.Account;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>What the parser reads from Fixtures/account-home-sample.html.</summary>
public static class SampleAccount
{
    public static readonly AccountSummary Summary =
        new(1234567, "LEGEND", "Membership Expired: Tuesday, January 2, 2030", 7);
}
