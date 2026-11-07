using System.Text;
using AqwCompanion.App.Reputation;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeReputationAutoSource : IReputationAutoSource
{
    private static readonly DateTimeOffset DefaultWriteTime = new(2030, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public string Path { get; set; } = "/fictional/AqwCompanion/reputation/reputation.json";

    /// <summary>What the next opens return; defaults to a missing file.</summary>
    public Func<ReputationAutoRead> OnOpen { get; set; } = () => new ReputationAutoRead(ReputationAutoStatus.Missing);

    public int OpenCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public ReputationAutoRead Open()
    {
        OpenCalls++;
        return OnOpen();
    }

    public void Delete() => DeleteCalls++;

    /// <summary>Makes the next opens return a file with this text, size and write time.</summary>
    public void Returns(string content, DateTimeOffset? lastWriteTimeUtc = null, long? length = null) =>
        OnOpen = () =>
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new ReputationAutoRead(
                ReputationAutoStatus.Found,
                new ReputationAutoFile(new MemoryStream(bytes), length ?? bytes.Length, lastWriteTimeUtc ?? DefaultWriteTime));
        };

    public void Missing() => OnOpen = () => new ReputationAutoRead(ReputationAutoStatus.Missing);
}
