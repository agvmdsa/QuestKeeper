using AqwCompanion.App.SignIn;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AqwCompanion.Tests.SignIn;

public class NavigationWatchdogTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Slow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly CollectingSink _logs = new();
    private readonly ILogger _logger;

    public NavigationWatchdogTests()
    {
        _logger = new LoggerConfiguration().WriteTo.Sink(_logs).CreateLogger();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }

    [Fact]
    public async Task Fires_the_callback_after_the_timeout_and_logs_a_warning()
    {
        var fired = 0;
        var watchdog = new NavigationWatchdog(Fast, _logger, () => false, () => fired++);

        watchdog.Start();
        await watchdog.Running!.WaitAsync(Wait);

        Assert.Equal(1, fired);
        Assert.Contains(_logs.Events, e =>
            e.Level == LogEventLevel.Warning
            && e.MessageTemplate.Text == "No page finished loading within {Timeout}");
    }

    [Fact]
    public async Task Does_not_fire_after_a_page_loaded()
    {
        var fired = 0;
        var watchdog = new NavigationWatchdog(Fast, _logger, () => false, () => fired++);

        watchdog.Start();
        var running = watchdog.Running!;
        Assert.True(watchdog.MarkPageLoaded());
        await running.WaitAsync(Wait);

        Assert.Equal(0, fired);
        Assert.True(watchdog.PageLoaded);
        Assert.Null(watchdog.Running);
    }

    [Fact]
    public async Task Stop_cancels_the_countdown()
    {
        var fired = 0;
        var watchdog = new NavigationWatchdog(Slow, _logger, () => false, () => fired++);

        watchdog.Start();
        var running = watchdog.Running!;
        watchdog.Stop();
        await running.WaitAsync(Wait);

        Assert.Equal(0, fired);
        Assert.Null(watchdog.Running);
        Assert.False(watchdog.PageLoaded);
    }

    [Fact]
    public void Start_after_a_page_loaded_does_nothing()
    {
        var watchdog = new NavigationWatchdog(Slow, _logger, () => false, () => { });
        watchdog.MarkPageLoaded();

        watchdog.Start();

        Assert.Null(watchdog.Running);
    }

    [Fact]
    public void Start_replaces_a_running_countdown()
    {
        var watchdog = new NavigationWatchdog(Slow, _logger, () => false, () => { });

        watchdog.Start();
        var first = watchdog.Running;
        watchdog.Start();

        Assert.NotNull(first);
        Assert.NotSame(first, watchdog.Running);
        Assert.True(first!.IsCompleted);
        watchdog.Stop();
    }

    [Fact]
    public void MarkPageLoaded_returns_true_only_the_first_time()
    {
        var watchdog = new NavigationWatchdog(Slow, _logger, () => false, () => { });

        Assert.True(watchdog.MarkPageLoaded());
        Assert.False(watchdog.MarkPageLoaded());
        Assert.True(watchdog.PageLoaded);
    }

    [Fact]
    public async Task A_stopped_flow_suppresses_the_callback()
    {
        var fired = 0;
        var watchdog = new NavigationWatchdog(Fast, _logger, () => true, () => fired++);

        watchdog.Start();
        await watchdog.Running!.WaitAsync(Wait);

        Assert.Equal(0, fired);
        Assert.DoesNotContain(_logs.Events, e => e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public async Task An_exception_from_the_callback_is_logged_and_does_not_escape()
    {
        var watchdog = new NavigationWatchdog(
            Fast, _logger, () => false, () => throw new InvalidOperationException("secret"));

        watchdog.Start();
        await watchdog.Running!.WaitAsync(Wait);

        var error = Assert.Single(_logs.Events, e => e.Level == LogEventLevel.Error);
        Assert.Equal("The navigation watchdog failed: {ExceptionType}", error.MessageTemplate.Text);
        Assert.DoesNotContain("secret", error.RenderMessage());
        Assert.Contains("InvalidOperationException", error.RenderMessage());
    }
}
