namespace AqwCompanion.Phone.Notices;

public enum NoticeKind { RankUp, HardFarmDrop, ActivityStopped, CaptureLost, CaptureBack, NewLogin, SkuaDisconnected, SkuaReconnected }

public sealed record Notice(string Id, NoticeKind Kind, string Character, string Title, string Body, DateTimeOffset AtUtc);

public sealed record NoticeOptions(TimeSpan StallAfter, int MaxPerCharacterPerMinute)
{
    public static NoticeOptions Default { get; } = new(TimeSpan.FromMinutes(10), 6);
}
