using System.IO.Pipes;
using System.Text;
using AqwCompanion.App.Skua;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.Tests.Skua;

public class SkuaPipeServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static string NewName() => "qk-test-" + Guid.NewGuid().ToString("N");

    private static string HelloLine => SkuaLine.ToLine(new HelloMessage(new SkuaHello("0.1.0", "1.4.5.0")));

    private static SkuaBridge NewBridge() => new(new FakeStore(), () => Noon);

    /// <summary>Waits until the condition holds, woken by the bridge's Changed event (never by polling).</summary>
    private static async Task WaitFor(SkuaBridge bridge, Func<bool> condition)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, EventArgs e)
        {
            if (condition())
            {
                tcs.TrySetResult();
            }
        }

        bridge.Changed += Handler;
        try
        {
            Handler(null, EventArgs.Empty);
            await tcs.Task.WaitAsync(Timeout);
        }
        finally
        {
            bridge.Changed -= Handler;
        }
    }

    /// <summary>Waits until the server has created its n-th pipe instance, so a client cannot meet the gap between two instances.</summary>
    private static async Task WaitListening(SkuaPipeServer server, int count)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, EventArgs e)
        {
            if (server.ListeningCount >= count)
            {
                tcs.TrySetResult();
            }
        }

        server.Listening += Handler;
        try
        {
            Handler(null, EventArgs.Empty);
            await tcs.Task.WaitAsync(Timeout);
        }
        finally
        {
            server.Listening -= Handler;
        }
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.Out);
        await client.ConnectAsync((int)Timeout.TotalMilliseconds);
        return client;
    }

    private static async Task WriteAsync(Stream client, string text)
    {
        await client.WriteAsync(Encoding.UTF8.GetBytes(text));
        await client.FlushAsync();
    }

    private static string LogLine(string text) =>
        SkuaLine.ToLine(new LogMessage(new SkuaLogEntry(Noon, SkuaLogKind.Sold, text)));

    [Fact]
    public async Task A_hello_connects_and_closing_the_client_disconnects()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();

        var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine + "\n");
        await WaitFor(bridge, () => bridge.Current.Hello is not null);
        Assert.Equal(SkuaConnection.Connected, bridge.Current.Connection);
        Assert.Equal(new SkuaHello("0.1.0", "1.4.5.0"), bridge.Current.Hello);

        await client.DisposeAsync();
        await WaitFor(bridge, () => bridge.Current.Connection == SkuaConnection.NotConnected);
    }

    [Fact]
    public async Task A_status_and_log_lines_arrive_in_order()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();

        await using var client = await ConnectAsync(name);
        var status = new SkuaStatus(true, "Hero", "Artix", "battleon", null, false, null);
        await WriteAsync(client, SkuaLine.ToLine(new StatusMessage(status)) + "\n" + LogLine("first") + "\n" + LogLine("second") + "\n");
        await WaitFor(bridge, () => bridge.Log.Count == 2);

        Assert.Equal(status, bridge.Current.Status);
        Assert.Equal(["second", "first"], bridge.Log.Select(e => e.Text));
    }

    [Fact]
    public async Task A_malformed_line_is_rejected_and_the_connection_keeps_working()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, "this is not json\n" + LogLine("good") + "\n");
        await WaitFor(bridge, () => bridge.Log.Count == 1);

        Assert.Equal(1, bridge.Rejected);
    }

    [Fact]
    public async Task An_oversize_line_is_rejected()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, new string('x', SkuaProtocol.MaxLineBytes + 10) + "\n" + LogLine("after") + "\n");
        await WaitFor(bridge, () => bridge.Log.Count == 1);

        Assert.Equal(1, bridge.Rejected);
    }

    [Fact]
    public async Task A_second_client_can_connect_after_the_first_left()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();

        await WaitListening(server, 1);
        var first = await ConnectAsync(name);
        await WriteAsync(first, LogLine("one") + "\n");
        await WaitFor(bridge, () => bridge.Log.Count == 1);
        await first.DisposeAsync();
        await WaitFor(bridge, () => bridge.Current.Connection == SkuaConnection.NotConnected);

        await WaitListening(server, 2);
        await using var second = await ConnectAsync(name);
        await WriteAsync(second, LogLine("two") + "\n");
        await WaitFor(bridge, () => bridge.Log.Count == 2);
    }

    [Fact]
    public async Task A_forget_closes_the_connection_so_the_plugin_reconnects_and_resends_its_status()
    {
        var name = NewName();
        var bridge = NewBridge();
        var errors = 0;
        await using var server = new SkuaPipeServer(name, bridge, _ => Interlocked.Increment(ref errors));
        server.Start();
        await WaitListening(server, 1);
        var status = new SkuaStatus(true, "Hero", "Artix", "battleon", null, false, null);
        var first = await ConnectAsync(name);
        await WriteAsync(first, HelloLine + "\n" + SkuaLine.ToLine(new StatusMessage(status)) + "\n");
        await WaitFor(bridge, () => bridge.Current.Status is not null);

        bridge.Quiesce();
        bridge.Forget();

        await WaitFor(bridge, () => bridge.Current.Connection == SkuaConnection.NotConnected);
        await first.DisposeAsync();
        Assert.Null(bridge.Current.Status);
        Assert.NotNull(bridge.Current.Hello);
        await WaitListening(server, 2);
        await using var second = await ConnectAsync(name);
        await WriteAsync(second, HelloLine + "\n" + SkuaLine.ToLine(new StatusMessage(status)) + "\n");
        await WaitFor(bridge, () => bridge.Current.Status == status && bridge.Current.Connection == SkuaConnection.Connected);
        Assert.Equal(0, Volatile.Read(ref errors));
    }

    [Fact]
    public async Task A_forget_while_no_client_is_connected_keeps_the_waiting_pipe()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();
        await WaitListening(server, 1);

        bridge.Forget();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine + "\n");
        await WaitFor(bridge, () => bridge.Current.Hello is not null);
        Assert.Equal(1, server.ListeningCount);
    }

    [Fact]
    public async Task Disposing_with_a_client_connected_returns_promptly_and_frees_the_name()
    {
        var name = NewName();
        var bridge = NewBridge();
        var server = new SkuaPipeServer(name, bridge);
        server.Start();
        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine + "\n");
        await WaitFor(bridge, () => bridge.Current.Hello is not null);

        var started = DateTime.UtcNow;
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.True(DateTime.UtcNow - started < Timeout);

        using var again = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    [Fact]
    public async Task Disposing_while_waiting_for_a_client_returns_promptly_and_is_safe_twice()
    {
        var server = new SkuaPipeServer(NewName(), NewBridge());
        server.Start();

        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    [Fact]
    public async Task Disposing_flushes_the_bridge()
    {
        var name = NewName();
        var store = new FakeStore();
        var bridge = new SkuaBridge(store, () => Noon);
        var server = new SkuaPipeServer(name, bridge);
        server.Start();
        // The first log entry saves at once (MinValue last save); the second one is still unsaved when the server stops.
        await using var client = await ConnectAsync(name);
        await WriteAsync(client, LogLine("a") + "\n" + LogLine("b") + "\n");
        await WaitFor(bridge, () => bridge.Log.Count == 2);

        await server.DisposeAsync();

        Assert.Equal(2, store.Stored.Count);
    }

    [Fact]
    public async Task A_line_of_another_version_sets_the_version_mismatch()
    {
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, "{\"v\":99,\"t\":\"hello\"}\n");
        await WaitFor(bridge, () => bridge.Current.Connection == SkuaConnection.VersionMismatch);
    }

    [Fact]
    public async Task The_server_pipe_is_inbound_only()
    {
        Assert.Equal(PipeDirection.In, SkuaPipeServer.Direction);

        // Observed on macOS (Unix domain sockets): an InOut client connects fine and is not refused, so the direction is enforced
        // by the Direction constant and by the server code never writing; on Windows the OS refuses it (not verified here). It is OS dependent; record what happens here
        // without depending on it: either the connect is refused or the connection comes up; the server code never writes either way.
        var name = NewName();
        var bridge = NewBridge();
        await using var server = new SkuaPipeServer(name, bridge);
        server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        try
        {
            await client.ConnectAsync((int)Timeout.TotalMilliseconds);
            await WriteAsync(client, HelloLine + "\n");
            await WaitFor(bridge, () => bridge.Current.Hello is not null);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or IOException or TimeoutException)
        {
            // Refused: the direction is enforced by the OS on this platform.
        }
    }

    [Fact]
    public async Task A_pipe_that_cannot_be_created_is_reported_once_and_does_not_throw()
    {
        var name = NewName();
        using var owner = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new SkuaPipeServer(name, NewBridge(), ex => reported.TrySetResult(ex));
        server.Start();

        await reported.Task.WaitAsync(Timeout);
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    [Fact]
    public async Task The_host_starts_once_and_the_service_provider_stops_it_on_dispose()
    {
        var name = NewName();
        var bridge = NewBridge();
        // Registered by factory like in the composition root: the container disposes what it created, not instances handed to it.
        var services = new ServiceCollection().AddSingleton(_ => new SkuaBridgeHost(bridge, null, name)).BuildServiceProvider();
        var host = services.GetRequiredService<SkuaBridgeHost>();
        host.Start();
        host.Start();
        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine + "\n");
        await WaitFor(bridge, () => bridge.Current.Hello is not null);

        services.Dispose();

        await WaitFor(bridge, () => bridge.Current.Connection == SkuaConnection.NotConnected);
        using var again = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    [Fact]
    public void On_Windows_the_pipe_belongs_to_the_user_sid_and_grants_only_that_sid()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Pipe security descriptors exist only on Windows; elsewhere the server uses CurrentUserOnly.
            return;
        }

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var security = SkuaPipeServer.CreateUserOnlySecurity();

        Assert.Equal(user, security.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToList();
        var rule = Assert.Single(rules);
        Assert.Equal(user, rule.IdentityReference);
        Assert.Equal(System.Security.AccessControl.AccessControlType.Allow, rule.AccessControlType);
    }

    private sealed class FakeStore : ISkuaLogStore
    {
        public List<SkuaLogEntry> Stored { get; private set; } = [];

        public IReadOnlyList<SkuaLogEntry> Load() => Stored;

        public void Save(IReadOnlyList<SkuaLogEntry> log) => Stored = [.. log];

        public void Delete() => Stored = [];
    }
}
