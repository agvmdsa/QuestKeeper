using AqwCompanion.Core.Forgetting;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>
/// The folder an older version read the exported reputation file from. The app no longer uses it, but Forgetting still removes
/// it so that "Forget my data" leaves nothing of the app behind except logs and exports. A failed delete is reported by name.
/// </summary>
public sealed class LegacyReputationFolder(AppPaths paths) : IForgettable
{
    public string Name => "Reputation file";

    public void Quiesce()
    {
    }

    public void Resume()
    {
    }

    public void Forget()
    {
        if (Directory.Exists(paths.ReputationDirectory))
        {
            Directory.Delete(paths.ReputationDirectory, recursive: true);
        }
    }
}
