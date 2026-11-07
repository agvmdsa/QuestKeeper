using System.Text;
using AqwCompanion.App.Reputation;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeReputationFilePicker : IReputationFilePicker
{
    /// <summary>What the picker hands out next; defaults to the user cancelling.</summary>
    public Func<Task<PickedFile?>> OnPick { get; set; } = () => Task.FromResult<PickedFile?>(null);

    public int PickCalls { get; private set; }

    public Task<PickedFile?> PickAsync()
    {
        PickCalls++;
        return OnPick();
    }

    /// <summary>Makes the next picks return a file with this text.</summary>
    public void Returns(string content, string name = "reputation.json") =>
        OnPick = () => Task.FromResult<PickedFile?>(new PickedFile(new MemoryStream(Encoding.UTF8.GetBytes(content)), name));
}
