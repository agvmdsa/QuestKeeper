using AqwCompanion.App;
using AqwCompanion.App.Infrastructure.Persistence;

namespace AqwCompanion.Tests.Persistence;

public sealed class LegacyReputationFolderTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public void Forgetting_removes_the_folder_an_older_version_used()
    {
        Directory.CreateDirectory(_paths.ReputationDirectory);
        File.WriteAllText(Path.Combine(_paths.ReputationDirectory, "reputation.json"), "{}");

        new LegacyReputationFolder(_paths).Forget();

        Assert.False(Directory.Exists(_paths.ReputationDirectory));
    }

    [Fact]
    public void Forgetting_without_the_folder_does_nothing()
    {
        var folder = new LegacyReputationFolder(_paths);

        folder.Quiesce();
        folder.Forget();
        folder.Resume();

        Assert.False(Directory.Exists(_paths.Root));
    }
}
