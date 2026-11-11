using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Tests.Phone;

public class NoticeLogTests
{
    private static Notice N(string id) => new(id, NoticeKind.NewLogin, "A", "t", "b", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Recent_is_newest_first_and_keeps_only_the_capacity()
    {
        var log = new NoticeLog(3);
        foreach (var id in new[] { "1", "2", "3", "4" })
        {
            log.Add(N(id));
        }

        Assert.Equal(["4", "3", "2"], log.Recent.Select(n => n.Id));
    }

    [Fact]
    public void Clear_empties_Recent()
    {
        var log = new NoticeLog(3);
        log.Add(N("1"));
        log.Add(N("2"));

        log.Clear();

        Assert.Empty(log.Recent);
    }
}
