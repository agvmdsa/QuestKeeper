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

    private static string HelloLine(string? id = null) => SkuaLine.ToLine(new HelloMessage(new SkuaHello("0.1.0", "1.4.5.0", id)));

    private static string StatusLine(string character) =>
        SkuaLine.ToLine(new StatusMessage(new SkuaStatus(true, character, "Artix", "battleon", null, false, null)));

    private static SkuaHub NewHub(SkuaHubTests.FakeStores? stores = null) => new(stores ?? new SkuaHubTests.FakeStores(), () => Noon);

    private static SkuaInstanceView? Find(SkuaHub hub, string id) => hub.Current.Instances.FirstOrDefault(i => i.InstanceId == id);

    /// <summary>Waits until the condition holds, woken by the hub's Changed event (never by polling).</summary>
    private static async Task WaitFor(SkuaHub hub, Func<bool> condition)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, EventArgs e)
        {
            if (condition())
            {
                tcs.TrySetResult();
            }
        }

        hub.Changed += Handler;
        try
        {
            Handler(null, EventArgs.Empty);
            await tcs.Task.WaitAsync(Timeout);
        }
        finally
        {
            hub.Changed -= Handler;
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

    private static async Task<NamedPipeClientStream> ConnectAsync(string name, PipeDirection direction = PipeDirection.InOut)
    {
        var client = new NamedPipeClientStream(".", name, direction, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)Timeout.TotalMilliseconds);
        return client;
    }

    private static async Task WriteAsync(Stream client, string text)
    {
        await client.WriteAsync(Encoding.UTF8.GetBytes(text));
        await client.FlushAsync();
    }

    /// <summary>Reads one line the server wrote to this client.</summary>
    private static async Task<string> ReadLineAsync(Stream client)
    {
        await using var e = BoundedLineReader.ReadAsync(client, CancellationToken.None, SkuaProtocol.MaxCommandLineBytes).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync().AsTask().WaitAsync(Timeout));
        return e.Current.Text!;
    }

    private static string LogLine(string text) =>
        SkuaLine.ToLine(new LogMessage(new SkuaLogEntry(Noon, SkuaLogKind.Sold, text)));

    [Fact]
    public async Task A_hello_connects_and_closing_the_client_disconnects()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n");
        await WaitFor(hub, () => Find(hub, "a") is not null);
        Assert.Equal(SkuaConnection.Connected, Find(hub, "a")!.Connection);
        Assert.Equal(new SkuaHello("0.1.0", "1.4.5.0", "a"), Find(hub, "a")!.Hello);

        await client.DisposeAsync();
        await WaitFor(hub, () => Find(hub, "a")!.Disconnected);
    }

    [Fact]
    public async Task A_status_and_log_lines_arrive_in_order()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n" + StatusLine("Hero") + "\n" + LogLine("first") + "\n" + LogLine("second") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Log.Count == 2);

        Assert.Equal("Hero", Find(hub, "a")!.Status!.Character);
        Assert.Equal(["second", "first"], Find(hub, "a")!.Log.Select(e => e.Text));
    }

    [Fact]
    public async Task Two_plugins_are_connected_at_once_with_their_own_state()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        await using var first = await ConnectAsync(name);
        await using var second = await ConnectAsync(name);
        await WriteAsync(first, HelloLine("a") + "\n" + StatusLine("Hero") + "\n");
        await WriteAsync(second, HelloLine("b") + "\n" + StatusLine("Zed") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Status is not null && Find(hub, "b")?.Status is not null);

        Assert.Equal("Hero", Find(hub, "a")!.Label);
        Assert.Equal("Zed", Find(hub, "b")!.Label);
        Assert.All(hub.Current.Instances, i => Assert.False(i.Disconnected));
    }

    [Fact]
    public async Task A_command_is_written_to_the_right_plugin_and_its_result_comes_back()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();
        await using var first = await ConnectAsync(name);
        await using var second = await ConnectAsync(name);
        await WriteAsync(first, HelloLine("a") + "\n");
        await WriteAsync(second, HelloLine("b") + "\n");
        await WaitFor(hub, () => Find(hub, "a") is not null && Find(hub, "b") is not null);

        var pending = hub.SendCommandAsync("b", new ListScriptsCommand(""), CancellationToken.None);
        var line = await ReadLineAsync(second);
        var command = Assert.IsType<ListScriptsCommand>(SkuaLine.Parse(line));
        // A result larger than the 16 KB line cap still arrives (the result cap is 1 MB).
        var scripts = Enumerable.Range(0, 400).Select(i => new SkuaScriptInfo($"{i:x16}", "Script number " + i, "Some folder")).ToList();
        var result = new ResultMessage(command.ReqId, true, null, scripts, null, null, false, false);
        var resultLine = SkuaLine.ToLine(result);
        Assert.True(Encoding.UTF8.GetByteCount(resultLine) > SkuaProtocol.MaxLineBytes);
        await WriteAsync(second, resultLine + "\n");

        Assert.Equal(result, await pending.WaitAsync(Timeout));
        Assert.Equal(0, hub.Rejected);

        // The first plugin got nothing: the next thing it reads is its own command.
        var other = hub.SendCommandAsync("a", new StopCommand(""), CancellationToken.None);
        Assert.IsType<StopCommand>(SkuaLine.Parse(await ReadLineAsync(first)));
        _ = other;
    }

    [Fact]
    public async Task A_plugin_that_never_reads_does_not_block_the_server()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();
        await using var client = await ConnectAsync(name, PipeDirection.Out);
        await WriteAsync(client, HelloLine("a") + "\n");
        await WaitFor(hub, () => Find(hub, "a") is not null);

        // Many commands queue up without anyone reading; none of the sends blocks the caller.
        var sends = Enumerable.Range(0, 100).Select(_ => hub.SendCommandAsync("a", new StopCommand(""), CancellationToken.None)).ToList();
        await WriteAsync(client, LogLine("still read") + "\n");
        await WaitFor(hub, () => Find(hub, "a")!.Log.Count == 1);
        Assert.All(sends, s => Assert.False(s.IsFaulted));
    }

    [Fact]
    public async Task The_ninth_plugin_is_closed_quietly()
    {
        var name = NewName();
        var hub = NewHub();
        var errors = 0;
        await using var server = new SkuaPipeServer(name, hub, _ => Interlocked.Increment(ref errors));
        server.Start();
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < SkuaProtocol.MaxInstances; i++)
            {
                var client = await ConnectAsync(name);
                clients.Add(client);
                await WriteAsync(client, HelloLine("id-" + i) + "\n");
            }

            await WaitFor(hub, () => hub.Current.Instances.Count == SkuaProtocol.MaxInstances);

            await using var ninth = await ConnectAsync(name);
            var buffer = new byte[16];
            var read = await ninth.ReadAsync(buffer).AsTask().WaitAsync(Timeout);
            Assert.Equal(0, read);
            Assert.Equal(SkuaProtocol.MaxInstances, hub.Current.Instances.Count);

            // A freed slot takes the next plugin.
            await clients[0].DisposeAsync();
            await WaitFor(hub, () => Find(hub, "id-0")!.Disconnected);
            await using var next = await ConnectAsync(name);
            await WriteAsync(next, HelloLine("id-9") + "\n");
            await WaitFor(hub, () => Find(hub, "id-9") is not null);
            Assert.Equal(0, Volatile.Read(ref errors));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task The_slot_is_free_before_the_hub_reports_the_disconnect()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();
        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n");
        await WaitFor(hub, () => Find(hub, "a") is not null);
        Assert.Equal(1, server.ActiveConnections);

        // Seen from inside the hub's Changed event, the very moment the disconnect is announced: the slot must be free already.
        var seen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Changed += (_, _) =>
        {
            if (Find(hub, "a")?.Disconnected == true)
            {
                seen.TrySetResult(server.ActiveConnections);
            }
        };
        await client.DisposeAsync();

        Assert.Equal(0, await seen.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Full_connect_and_disconnect_cycles_always_find_a_free_slot_and_a_listening_pipe()
    {
        var name = NewName();
        var hub = NewHub();
        var errors = 0;
        await using var server = new SkuaPipeServer(name, hub, _ => Interlocked.Increment(ref errors));
        server.Start();
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < SkuaProtocol.MaxInstances; i++)
            {
                var client = await ConnectAsync(name);
                clients.Add(client);
                await WriteAsync(client, HelloLine("c" + i) + "\n");
            }

            await WaitFor(hub, () => hub.Current.Instances.Count == SkuaProtocol.MaxInstances);

            for (var round = 0; round < 25; round++)
            {
                var index = round % SkuaProtocol.MaxInstances;
                var id = "c" + index;
                await clients[index].DisposeAsync();
                await WaitFor(hub, () => Find(hub, id)!.Disconnected);

                var next = await ConnectAsync(name);
                clients[index] = next;
                await WriteAsync(next, HelloLine(id) + "\n" + LogLine("round " + round) + "\n");
                await WaitFor(hub, () => Find(hub, id)!.Log.Any(e => e.Text == "round " + round) && !Find(hub, id)!.Disconnected);
            }

            Assert.Equal(SkuaProtocol.MaxInstances, hub.Current.Instances.Count);
            Assert.Equal(0, Volatile.Read(ref errors));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task A_malformed_line_is_rejected_and_the_connection_keeps_working()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\nthis is not json\n" + LogLine("good") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Log.Count == 1);

        Assert.Equal(1, hub.Rejected);
    }

    [Fact]
    public async Task An_oversize_line_is_rejected()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n" + new string('x', SkuaProtocol.MaxResultLineBytes + 10) + "\n" + LogLine("after") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Log.Count == 1);

        Assert.Equal(1, hub.Rejected);
    }

    [Fact]
    public async Task A_plugin_that_reconnects_gets_its_instance_back()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        await WaitListening(server, 1);
        var first = await ConnectAsync(name);
        await WriteAsync(first, HelloLine("a") + "\n" + LogLine("one") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Log.Count == 1);
        await first.DisposeAsync();
        await WaitFor(hub, () => Find(hub, "a")!.Disconnected);

        await using var second = await ConnectAsync(name);
        await WriteAsync(second, HelloLine("a") + "\n" + LogLine("two") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Log.Count == 2 && !Find(hub, "a")!.Disconnected);
        Assert.Single(hub.Current.Instances);
    }

    [Fact]
    public async Task A_forget_closes_every_connection_so_the_plugins_reconnect_and_resend_their_status()
    {
        var name = NewName();
        var hub = NewHub();
        var errors = 0;
        await using var server = new SkuaPipeServer(name, hub, _ => Interlocked.Increment(ref errors));
        server.Start();
        await using var first = await ConnectAsync(name);
        await using var second = await ConnectAsync(name);
        await WriteAsync(first, HelloLine("a") + "\n" + StatusLine("Hero") + "\n");
        await WriteAsync(second, HelloLine("b") + "\n" + StatusLine("Zed") + "\n");
        await WaitFor(hub, () => hub.Current.Instances.Count(i => i.Status is not null) == 2);

        hub.Quiesce();
        hub.Forget();

        Assert.Empty(hub.Current.Instances);
        var buffer = new byte[16];
        Assert.Equal(0, await first.ReadAsync(buffer).AsTask().WaitAsync(Timeout));
        Assert.Equal(0, await second.ReadAsync(buffer).AsTask().WaitAsync(Timeout));
        await using var again = await ConnectAsync(name);
        await WriteAsync(again, HelloLine("a") + "\n" + StatusLine("Hero") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Status is not null);
        Assert.Equal(0, Volatile.Read(ref errors));
    }

    [Fact]
    public async Task A_forget_while_no_client_is_connected_keeps_the_waiting_pipe()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();
        await WaitListening(server, 1);

        hub.Forget();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n");
        await WaitFor(hub, () => Find(hub, "a") is not null);
        await WaitListening(server, 2);
        Assert.Equal(2, server.ListeningCount);   // the first pipe took the client, the second is the spare
    }

    [Fact]
    public async Task Disposing_with_clients_connected_returns_promptly_and_frees_the_name()
    {
        var name = NewName();
        var hub = NewHub();
        var server = new SkuaPipeServer(name, hub);
        server.Start();
        await using var client = await ConnectAsync(name);
        await using var silent = await ConnectAsync(name, PipeDirection.Out);
        await WriteAsync(client, HelloLine("a") + "\n");
        await WriteAsync(silent, HelloLine("b") + "\n");
        await WaitFor(hub, () => hub.Current.Instances.Count == 2);
        _ = hub.SendCommandAsync("b", new StopCommand(""), CancellationToken.None);

        var started = DateTime.UtcNow;
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.True(DateTime.UtcNow - started < Timeout);
        Assert.All(hub.Current.Instances, i => Assert.True(i.Disconnected));

        using var again = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    [Fact]
    public async Task Disposing_while_waiting_for_a_client_returns_promptly_and_is_safe_twice()
    {
        var server = new SkuaPipeServer(NewName(), NewHub());
        server.Start();

        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    [Fact]
    public async Task Disposing_flushes_the_logs()
    {
        var name = NewName();
        var stores = new SkuaHubTests.FakeStores();
        var hub = NewHub(stores);
        var server = new SkuaPipeServer(name, hub);
        server.Start();
        // The first log entry saves at once (MinValue last save); the second one is still unsaved when the server stops.
        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n" + StatusLine("Hero") + "\n" + LogLine("a") + "\n" + LogLine("b") + "\n");
        await WaitFor(hub, () => Find(hub, "a")?.Log.Count == 2);

        await server.DisposeAsync();

        Assert.Equal(2, stores.For("Hero").Stored.Count);
    }

    [Fact]
    public async Task A_line_of_another_version_sets_the_version_mismatch()
    {
        var name = NewName();
        var hub = NewHub();
        await using var server = new SkuaPipeServer(name, hub);
        server.Start();

        await using var client = await ConnectAsync(name);
        await WriteAsync(client, "{\"v\":99,\"t\":\"hello\"}\n");
        await WaitFor(hub, () => hub.Current.Primary.Connection == SkuaConnection.VersionMismatch);
    }

    [Fact]
    public void The_server_pipe_is_two_way()
    {
        Assert.Equal(PipeDirection.InOut, SkuaPipeServer.Direction);
    }

    [Fact]
    public async Task A_pipe_that_cannot_be_created_is_reported_once_and_does_not_throw()
    {
        var name = NewName();
        using var owner = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var reports = 0;
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new SkuaPipeServer(name, NewHub(), ex =>
        {
            Interlocked.Increment(ref reports);
            reported.TrySetResult(ex);
        });
        server.Start();

        await reported.Task.WaitAsync(Timeout);
        await Task.Delay(SkuaPipeServer.RetryDelay * 3);
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(1, Volatile.Read(ref reports));
    }

    [Fact]
    public async Task The_host_starts_once_and_the_service_provider_stops_it_on_dispose()
    {
        var name = NewName();
        var hub = NewHub();
        // Registered by factory like in the composition root: the container disposes what it created, not instances handed to it.
        var services = new ServiceCollection().AddSingleton(_ => new SkuaBridgeHost(hub, null, name)).BuildServiceProvider();
        var host = services.GetRequiredService<SkuaBridgeHost>();
        host.Start();
        host.Start();
        await using var client = await ConnectAsync(name);
        await WriteAsync(client, HelloLine("a") + "\n");
        await WaitFor(hub, () => Find(hub, "a") is not null);

        services.Dispose();

        await WaitFor(hub, () => Find(hub, "a")!.Disconnected);
        using var again = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    [Fact]
    public void On_Windows_the_pipe_belongs_to_the_user_sid_and_grants_only_that_sid()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Pipe security descriptors exist only on Windows; elsewhere the server uses CurrentUserOnly. This test therefore
            // cannot run on the macOS development machine; it runs on the Windows CI.
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
        Assert.Equal(PipeAccessRights.FullControl, rule.PipeAccessRights);

        // A real pipe with that descriptor reads back the user SID as owner (on an elevated runner too, where the token's
        // default owner would be BUILTIN\Administrators).
        using var pipe = NamedPipeServerStreamAcl.Create(
            NewName(), SkuaPipeServer.Direction, SkuaPipeServer.MaxPipes, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        Assert.Equal(user, pipe.GetAccessControl().GetOwner(typeof(System.Security.Principal.SecurityIdentifier)));
    }
}
