using AqwCompanion.App;

namespace AqwCompanion.Tests.Infrastructure;

public class AppPathsTests
{
    private static string DefaultRoot(string appName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);

    [Fact]
    public void Without_an_override_the_local_app_data_folder_is_used()
    {
        Assert.Equal(DefaultRoot("AqwTest"), AppPaths.ForCurrentUser("AqwTest").Root);
    }

    [Fact]
    public void Existing_override_directory_becomes_the_root()
    {
        var dir = Directory.CreateTempSubdirectory("aqw-root-");
        try
        {
            Assert.Equal(dir.FullName, AppPaths.ForCurrentUser("AqwTest", dir.FullName).Root);
        }
        finally
        {
            dir.Delete();
        }
    }

    [Fact]
    public void Missing_override_directory_is_ignored()
    {
        var missing = Path.Combine(Path.GetTempPath(), "aqw-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(DefaultRoot("AqwTest"), AppPaths.ForCurrentUser("AqwTest", missing).Root);
    }

    [Fact]
    public void Null_override_uses_the_default()
    {
        Assert.Equal(DefaultRoot("AqwTest"), AppPaths.ForCurrentUser("AqwTest", null).Root);
    }
}
