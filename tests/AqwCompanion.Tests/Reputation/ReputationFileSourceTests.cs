using System.Text;
using AqwCompanion.App;
using AqwCompanion.App.Reputation;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.Tests.Reputation;

public sealed class ReputationFileSourceTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    private ReputationFileSource Source() => new(_paths);

    private void Write(byte[] bytes)
    {
        Directory.CreateDirectory(_paths.ReputationDirectory);
        File.WriteAllBytes(Path.Combine(_paths.ReputationDirectory, "reputation.json"), bytes);
    }

    [Fact]
    public void The_path_is_reputation_json_in_the_reputation_folder()
    {
        Assert.Equal(Path.Combine(_paths.Root, "reputation", "reputation.json"), Source().Path);
    }

    [Fact]
    public void A_missing_file_and_a_missing_folder_are_reported_as_missing()
    {
        var read = Source().Open();

        Assert.Equal(ReputationAutoStatus.Missing, read.Status);
        Assert.Null(read.File);
    }

    [Fact]
    public async Task A_normal_file_is_opened_with_its_size_and_write_time()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"schema":1,"factions":[{"factionId":1,"name":"Good","rank":1,"rep":1,"repToRank":1}]}""");
        Write(bytes);
        var stamp = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Source().Path, stamp);

        var read = Source().Open();

        Assert.Equal(ReputationAutoStatus.Found, read.Status);
        await using var content = read.File!.Content;
        Assert.Equal(bytes.Length, read.File.Length);
        Assert.Equal(new DateTimeOffset(stamp), read.File.LastWriteTimeUtc);
        Assert.NotNull((await ReputationParser.ParseAsync(content)).Snapshot);
    }

    [Fact]
    public async Task An_oversize_file_reports_its_length_so_it_can_be_refused_before_reading()
    {
        Write(new byte[ReputationParser.MaxBytes + 10]);

        var read = Source().Open();

        await using var content = read.File!.Content;
        Assert.Equal(ReputationParser.MaxBytes + 10, read.File.Length);
        Assert.Contains("too large", (await ReputationParser.ParseAsync(content)).Error);
    }

    [Fact]
    public void A_file_that_the_client_has_open_for_writing_can_still_be_read()
    {
        Write([]);
        using var writer = new FileStream(Source().Path, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Write);
        writer.Write(Encoding.UTF8.GetBytes("{}"));
        writer.Flush();

        var read = Source().Open();

        Assert.Equal(ReputationAutoStatus.Found, read.Status);
        using var content = read.File!.Content;
        Assert.Equal(2, read.File.Length);
    }

    [Fact]
    public void Delete_removes_the_folder_contents_and_does_not_throw_when_there_is_nothing()
    {
        Source().Delete();
        Write(Encoding.UTF8.GetBytes("{}"));
        File.WriteAllText(Path.Combine(_paths.ReputationDirectory, "other.txt"), "x");

        Source().Delete();

        Assert.False(Directory.Exists(_paths.ReputationDirectory));
        Assert.Equal(ReputationAutoStatus.Missing, Source().Open().Status);
    }
}
