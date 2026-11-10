namespace AqwCompanion.Tests.TestDoubles;

public static class Wait
{
    /// <summary>Polls until the condition holds. The ceiling is generous on purpose: it only matters when the test is already failing.</summary>
    public static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }
}
