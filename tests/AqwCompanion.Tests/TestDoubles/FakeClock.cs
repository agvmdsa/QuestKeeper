namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeClock(DateTimeOffset start)
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now += by;

    public DateTimeOffset Read() => Now;
}
