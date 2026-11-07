namespace AqwCompanion.Core.Forgetting;

/// <summary>
/// Orders Forgetting: quiesce every participant, remove the stored data (the gate), then forget memory and side files,
/// or resume when the removal failed. Never throws.
/// </summary>
public sealed class ForgetCoordinator(Func<bool> removeStoredData, Func<IEnumerable<IForgettable>> participants)
{
    /// <summary>Runs the three phases and reports what could not be forgotten.</summary>
    public ForgetResult Forget()
    {
        var list = participants().ToList();
        var failures = new List<string>();
        var failedToStop = new HashSet<IForgettable>();

        foreach (var participant in list)
        {
            try
            {
                participant.Quiesce();
            }
            catch (Exception)
            {
                failedToStop.Add(participant);
                failures.Add($"{participant.Name} (could not be stopped)");
            }
        }

        var removed = RemoveStoredData();
        if (!removed)
        {
            foreach (var participant in list)
            {
                try
                {
                    participant.Resume();
                }
                catch (Exception)
                {
                    // Resuming is best effort; the removal failure is what the caller reports.
                }
            }

            return new ForgetResult(false, []);
        }

        foreach (var participant in list)
        {
            try
            {
                participant.Forget();
            }
            catch (Exception)
            {
                if (!failedToStop.Contains(participant))
                {
                    failures.Add(participant.Name);
                }
            }
        }

        return new ForgetResult(true, failures);
    }

    private bool RemoveStoredData()
    {
        try
        {
            return removeStoredData();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
