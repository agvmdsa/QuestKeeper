using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Forgetting;

public sealed class ForgetCoordinatorTests
{
    [Fact]
    public void Quiesces_then_removes_then_forgets_in_that_order()
    {
        var log = new List<string>();
        var a = new RecordingForgettable("A", log);
        var b = new RecordingForgettable("B", log);
        var coordinator = new ForgetCoordinator(() => { log.Add("Remove"); return true; }, () => [a, b]);

        var result = coordinator.Forget();

        Assert.Equal(["Quiesce:A", "Quiesce:B", "Remove", "Forget:A", "Forget:B"], log);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void When_the_stored_data_removal_fails_nothing_is_forgotten_and_everything_resumes()
    {
        var log = new List<string>();
        var a = new RecordingForgettable("A", log);
        var b = new RecordingForgettable("B", log);
        var coordinator = new ForgetCoordinator(() => false, () => [a, b]);

        var result = coordinator.Forget();

        Assert.False(result.StoredDataRemoved);
        Assert.Empty(result.NotForgotten);
        Assert.Equal(["Quiesce:A", "Quiesce:B", "Resume:A", "Resume:B"], log);
    }

    [Fact]
    public void When_the_removal_throws_it_counts_as_failed()
    {
        var log = new List<string>();
        var a = new RecordingForgettable("A", log) { ThrowOnResume = true };
        var coordinator = new ForgetCoordinator(() => throw new IOException("disk"), () => [a]);

        var result = coordinator.Forget();

        Assert.False(result.StoredDataRemoved);
        Assert.False(result.IsComplete);
        Assert.Equal(["Quiesce:A", "Resume:A"], log);
    }

    [Fact]
    public void A_participant_that_throws_in_Forget_is_reported_and_the_others_still_forget()
    {
        var log = new List<string>();
        var a = new RecordingForgettable("A", log) { ThrowOnForget = true };
        var b = new RecordingForgettable("B", log);
        var coordinator = new ForgetCoordinator(() => true, () => [a, b]);

        var result = coordinator.Forget();

        Assert.True(result.StoredDataRemoved);
        Assert.Equal(["A"], result.NotForgotten);
        Assert.Contains("Forget:B", log);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void A_participant_that_throws_in_Quiesce_is_reported_once_even_if_Forget_also_throws()
    {
        var log = new List<string>();
        var a = new RecordingForgettable("A", log) { ThrowOnQuiesce = true, ThrowOnForget = true };
        var coordinator = new ForgetCoordinator(() => true, () => [a]);

        var result = coordinator.Forget();

        Assert.Single(result.NotForgotten);
        Assert.Equal("A (could not be stopped)", result.NotForgotten[0]);
        Assert.Contains("Forget:A", log);
    }

    [Fact]
    public void The_participants_are_asked_for_each_time_Forget_is_called()
    {
        var asked = 0;
        var coordinator = new ForgetCoordinator(() => true, () => { asked++; return []; });

        coordinator.Forget();
        coordinator.Forget();

        Assert.Equal(2, asked);
    }

    [Fact]
    public void Result_is_complete_only_when_removed_and_nothing_failed()
    {
        Assert.True(new ForgetResult(true, []).IsComplete);
        Assert.False(new ForgetResult(false, []).IsComplete);
        Assert.False(new ForgetResult(true, ["A"]).IsComplete);
    }
}
