using AqwCompanion.Core.Forgetting;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Records each call as "Quiesce:Name", "Resume:Name" or "Forget:Name" in a shared list, and can be told to throw.</summary>
public sealed class RecordingForgettable(string name, List<string> log) : IForgettable
{
    public string Name { get; } = name;

    public bool ThrowOnQuiesce { get; set; }

    public bool ThrowOnResume { get; set; }

    public bool ThrowOnForget { get; set; }

    public void Quiesce()
    {
        log.Add($"Quiesce:{Name}");
        if (ThrowOnQuiesce)
        {
            throw new InvalidOperationException("quiesce failed");
        }
    }

    public void Resume()
    {
        log.Add($"Resume:{Name}");
        if (ThrowOnResume)
        {
            throw new InvalidOperationException("resume failed");
        }
    }

    public void Forget()
    {
        log.Add($"Forget:{Name}");
        if (ThrowOnForget)
        {
            throw new InvalidOperationException("forget failed");
        }
    }
}
