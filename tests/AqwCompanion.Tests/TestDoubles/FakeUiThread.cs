using System.Collections.Concurrent;
using AqwCompanion.App.Browser;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A stand-in UI thread: one dedicated thread that runs posted work in order.</summary>
internal sealed class FakeUiThread : IUiThread
{
    private readonly BlockingCollection<Action> _queue = new();

    public FakeUiThread()
    {
        var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            ThreadId = Environment.CurrentManagedThreadId;
            started.Set();
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        })
        {
            IsBackground = true,
            Name = "FakeUiThread",
        };
        thread.Start();
        started.Wait();
    }

    public int ThreadId { get; private set; }

    /// <summary>Number of times work had to be queued because the caller was not on the UI thread.</summary>
    public int PostedCount { get; private set; }

    public bool CheckAccess() => Environment.CurrentManagedThreadId == ThreadId;

    public void Post(Action action)
    {
        PostedCount++;
        _queue.Add(action);
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        if (CheckAccess())
        {
            return work();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try
            {
                completion.SetResult(await work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread without counting it as a marshalled post.</summary>
    public Task<T> RunOnUiThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    public Task RunOnUiThread(Action work) =>
        RunOnUiThread(() =>
        {
            work();
            return 0;
        });

    /// <summary>Completes once everything queued so far has run.</summary>
    public Task Flush() => RunOnUiThread(() => 0);
}
