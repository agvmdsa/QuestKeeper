using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaHubTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStores _stores = new();
    private readonly FakeTimer _timer = new();
    private DateTimeOffset _now = T0;

    private SkuaHub Create() => new(_stores, () => _now, _timer.Delay);

    private static HelloMessage Hello(string? id) => new(new SkuaHello("0.5.0", "1.4.5.0", id));

    private static StatusMessage Status(string? character, string? map = "battleon") =>
        new(new SkuaStatus(character is not null, character, "Artix", map, null, false, null));

    private static LogMessage Log(string text, DateTimeOffset at) => new(new SkuaLogEntry(at, SkuaLogKind.Sold, text));

    private static (IDisposable Attachment, FakeConnection Connection) Connect(SkuaHub hub, string? id, string? character = null)
    {
        var connection = new FakeConnection();
        var attachment = hub.Attach(connection) ?? throw new InvalidOperationException("refused");
        hub.Accept(attachment, Hello(id));
        if (character is not null)
        {
            hub.Accept(attachment, Status(character));
        }

        return (attachment, connection);
    }

    private static SkuaInstanceView Only(SkuaHub hub) => Assert.Single(hub.Current.Instances);

    // ---- instances and labels ----

    [Fact]
    public void A_new_hub_has_no_instances_and_a_not_connected_primary_view()
    {
        var hub = Create();

        Assert.Empty(hub.Current.Instances);
        Assert.Equal(SkuaView.Empty, hub.Current.Primary);
    }

    [Fact]
    public void An_attachment_without_any_message_shows_no_instance()
    {
        var hub = Create();

        Assert.NotNull(hub.Attach(new FakeConnection()));

        Assert.Empty(hub.Current.Instances);
    }

    [Fact]
    public void Two_hellos_make_two_instances_labelled_in_order_of_arrival_and_named_after_their_characters()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a");
        var (b, _) = Connect(hub, "id-b");

        Assert.Equal(["Skua 1", "Skua 2"], hub.Current.Instances.Select(i => i.Label));
        Assert.Equal(["id-a", "id-b"], hub.Current.Instances.Select(i => i.InstanceId));
        Assert.All(hub.Current.Instances, i => Assert.Equal(SkuaConnection.Connected, i.Connection));
        Assert.All(hub.Current.Instances, i => Assert.False(i.Disconnected));

        hub.Accept(b, Status("Zed"));
        hub.Accept(a, Status("Hero"));

        Assert.Equal(["Hero", "Zed"], hub.Current.Instances.Select(i => i.Label));
    }

    [Fact]
    public void Instances_keep_their_own_status_and_log()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        var (b, _) = Connect(hub, "id-b", "Zed");

        hub.Accept(a, Log("sold by hero", T0));
        hub.Accept(b, Log("sold by zed", T0));
        hub.Accept(b, Status("Zed", "yulgar"));

        var hero = hub.Current.Instances[0];
        var zed = hub.Current.Instances[1];
        Assert.Equal("battleon", hero.Status!.Map);
        Assert.Equal("yulgar", zed.Status!.Map);
        Assert.Equal(["sold by hero"], hero.Log.Select(e => e.Text));
        Assert.Equal(["sold by zed"], zed.Log.Select(e => e.Text));
        Assert.Equal(new SkuaHello("0.5.0", "1.4.5.0", "id-a"), hero.Hello);
    }

    [Fact]
    public void A_logged_out_status_keeps_the_last_character_as_the_label()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");

        hub.Accept(a, Status(null));

        Assert.Equal("Hero", Only(hub).Label);
    }

    [Fact]
    public void An_older_plugin_without_an_id_gets_a_generated_one()
    {
        var hub = Create();
        Connect(hub, null);
        Connect(hub, null);

        var ids = hub.Current.Instances.Select(i => i.InstanceId).ToList();
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void A_status_before_any_hello_still_makes_an_instance()
    {
        var hub = Create();
        var attachment = hub.Attach(new FakeConnection())!;

        hub.Accept(attachment, Status("Hero"));

        Assert.Equal("Hero", Only(hub).Label);
    }

    [Fact]
    public void A_line_of_another_version_shows_a_version_mismatch()
    {
        var hub = Create();
        var attachment = hub.Attach(new FakeConnection())!;

        hub.Accept(attachment, new UnsupportedVersionMessage(99));

        Assert.Equal(SkuaConnection.VersionMismatch, Only(hub).Connection);
        Assert.Equal(SkuaConnection.VersionMismatch, hub.Current.Primary.Connection);
    }

    [Fact]
    public void Every_change_raises_changed_and_a_handler_can_read_the_view()
    {
        var hub = Create();
        var seen = new List<int>();
        hub.Changed += (_, _) => seen.Add(hub.Current.Instances.Count);

        var (a, _) = Connect(hub, "id-a");
        a.Dispose();

        Assert.Equal([1, 1], seen);
    }

    [Fact]
    public void A_handler_that_throws_does_not_stop_the_others()
    {
        var hub = Create();
        var reached = false;
        hub.Changed += (_, _) => throw new InvalidOperationException("bad");
        hub.Changed += (_, _) => reached = true;

        Connect(hub, "id-a");

        Assert.True(reached);
    }

    // ---- disconnect, reconnect, takeover ----

    [Fact]
    public void A_closed_pipe_marks_the_instance_disconnected_and_it_stays_with_its_status()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");

        a.Dispose();

        var instance = Only(hub);
        Assert.True(instance.Disconnected);
        Assert.Equal(SkuaConnection.NotConnected, instance.Connection);
        Assert.Equal("Hero", instance.Label);
        Assert.Equal("Hero", instance.Status!.Character);
    }

    [Fact]
    public void A_hello_with_the_same_id_reattaches_the_same_instance()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        hub.Accept(a, Log("before", T0));
        a.Dispose();

        Connect(hub, "id-a");

        var instance = Only(hub);
        Assert.Equal("id-a", instance.InstanceId);
        Assert.False(instance.Disconnected);
        Assert.Equal(SkuaConnection.Connected, instance.Connection);
        Assert.Equal(["before"], instance.Log.Select(e => e.Text));
    }

    [Fact]
    public void A_hello_with_the_id_of_a_connected_instance_moves_it_to_the_newer_pipe()
    {
        var hub = Create();
        var (old, _) = Connect(hub, "id-a", "Hero");

        var (fresh, connection) = Connect(hub, "id-a");
        hub.Accept(old, Log("late line of the old pipe", T0));
        old.Dispose();

        var instance = Only(hub);
        Assert.False(instance.Disconnected);
        Assert.Empty(instance.Log);
        _ = hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None);
        Assert.IsType<StopCommand>(Assert.Single(connection.Sent));
        fresh.Dispose();
        Assert.True(Only(hub).Disconnected);
    }

    [Fact]
    public void A_new_connection_whose_character_matches_a_disconnected_instance_takes_it_over()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        hub.Accept(a, Log("old run", T0));
        a.Dispose();

        var (b, connection) = Connect(hub, "id-b");
        hub.Accept(b, Log("before login", T0.AddMinutes(1)));
        Assert.Equal(2, hub.Current.Instances.Count);
        hub.Accept(b, Status("hero"));

        var instance = Only(hub);
        Assert.Equal("id-a", instance.InstanceId);
        Assert.False(instance.Disconnected);
        Assert.Equal(["before login", "old run"], instance.Log.Select(e => e.Text));
        Assert.Equal("hero", instance.Status!.Character);

        _ = hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None);
        Assert.Single(connection.Sent);

        // The plugin of the new connection reconnects with its own id and finds the same instance.
        b.Dispose();
        Connect(hub, "id-b");
        Assert.False(Only(hub).Disconnected);
        Assert.Equal("id-a", Only(hub).InstanceId);
    }

    [Fact]
    public void A_connected_instance_with_the_same_character_is_not_taken_over()
    {
        var hub = Create();
        Connect(hub, "id-a", "Hero");
        Connect(hub, "id-b", "Hero");

        Assert.Equal(2, hub.Current.Instances.Count);
    }

    [Fact]
    public void At_most_eight_connections_are_attached_and_the_ninth_is_refused()
    {
        var hub = Create();
        var attached = Enumerable.Range(0, SkuaProtocol.MaxInstances).Select(i => Connect(hub, "id-" + i).Attachment).ToList();

        Assert.Null(hub.Attach(new FakeConnection()));
        Assert.Equal(8, hub.Current.Instances.Count);

        attached[0].Dispose();
        Assert.NotNull(hub.Attach(new FakeConnection()));
    }

    [Fact]
    public void Old_disconnected_instances_are_dropped_beyond_the_retained_limit()
    {
        var hub = Create();
        for (var i = 0; i < SkuaHub.MaxRetained + 3; i++)
        {
            Connect(hub, "id-" + i).Attachment.Dispose();
        }

        Assert.Equal(SkuaHub.MaxRetained, hub.Current.Instances.Count);
        Assert.Equal("id-3", hub.Current.Instances[0].InstanceId);
    }

    [Fact]
    public void The_primary_view_is_the_first_connected_instance_else_the_first()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        var (b, _) = Connect(hub, "id-b", "Zed");
        Assert.Equal("Hero", hub.Current.Primary.Status!.Character);

        a.Dispose();
        Assert.Equal("Zed", hub.Current.Primary.Status!.Character);
        Assert.Equal(SkuaConnection.Connected, hub.Current.Primary.Connection);

        b.Dispose();
        Assert.Equal("Hero", hub.Current.Primary.Status!.Character);
        Assert.Equal(SkuaConnection.NotConnected, hub.Current.Primary.Connection);
    }

    [Fact]
    public void A_message_from_a_closed_attachment_is_ignored()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        a.Dispose();

        hub.Accept(a, Status("Hero", "yulgar"));

        Assert.Equal("battleon", Only(hub).Status!.Map);
    }

    [Fact]
    public void A_rejected_line_is_counted()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a");

        hub.Reject(a);

        Assert.Equal(1, hub.Rejected);
    }

    // ---- commands ----

    [Fact]
    public async Task A_command_goes_only_to_its_instance_with_a_fresh_req_id_and_completes_with_the_matching_result()
    {
        var hub = Create();
        var (a, first) = Connect(hub, "id-a");
        var (b, second) = Connect(hub, "id-b");

        var pending = hub.SendCommandAsync("id-b", new ListScriptsCommand("caller"), CancellationToken.None);

        Assert.Empty(first.Sent);
        var sent = Assert.IsType<ListScriptsCommand>(Assert.Single(second.Sent));
        Assert.NotEqual("caller", sent.ReqId);
        Assert.False(string.IsNullOrEmpty(sent.ReqId));

        var result = new ResultMessage(sent.ReqId, true, null, [new SkuaScriptInfo("abc", "Farm", "Farming")], null, null, false, false);
        hub.Accept(a, result);   // the same reqId from another instance is not its answer
        Assert.False(pending.IsCompleted);
        hub.Accept(b, result);

        Assert.Equal(result, await pending);
    }

    [Fact]
    public void Each_command_gets_its_own_req_id()
    {
        var hub = Create();
        var (_, connection) = Connect(hub, "id-a");

        _ = hub.SendCommandAsync("id-a", new StopCommand("x"), CancellationToken.None);
        _ = hub.SendCommandAsync("id-a", new StopCommand("x"), CancellationToken.None);

        Assert.Equal(2, connection.Sent.Cast<SkuaCommand>().Select(c => c.ReqId).Distinct().Count());
    }

    [Fact]
    public async Task No_answer_within_fifteen_seconds_completes_with_null_and_a_late_result_is_ignored()
    {
        var hub = Create();
        var (a, connection) = Connect(hub, "id-a");

        var pending = hub.SendCommandAsync("id-a", new ListScriptsCommand(""), CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(_timer.Requested));
        Assert.False(pending.IsCompleted);

        _timer.Fire();

        Assert.Null(await pending);
        var reqId = ((SkuaCommand)connection.Sent[0]).ReqId;
        hub.Accept(a, new ResultMessage(reqId, true, null, null, null, null, false, false));
    }

    [Fact]
    public void A_start_waits_forty_five_seconds_and_every_other_command_fifteen()
    {
        var hub = Create();
        Connect(hub, "id-a");

        _ = hub.SendCommandAsync("id-a", new StartCommand("", "abc"), CancellationToken.None);
        _ = hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None);
        _ = hub.SendCommandAsync("id-a", new GetOptionsCommand("", "abc"), CancellationToken.None);
        _ = hub.SendCommandAsync("id-a", new SetOptionsCommand("", "abc", []), CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15)], _timer.Requested);
        Assert.Equal(TimeSpan.FromSeconds(45), SkuaHub.StartTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), SkuaHub.CommandTimeout);
    }

    [Fact]
    public async Task A_command_for_an_unknown_instance_throws_key_not_found()
    {
        var hub = Create();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => hub.SendCommandAsync("nobody", new StopCommand(""), CancellationToken.None));
    }

    [Fact]
    public async Task A_command_for_a_disconnected_instance_throws_invalid_operation()
    {
        var hub = Create();
        var (a, connection) = Connect(hub, "id-a");
        a.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None));
        Assert.Empty(connection.Sent);
    }

    [Fact]
    public async Task A_disconnect_while_waiting_completes_the_command_with_null_at_once()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a");
        var pending = hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None);

        a.Dispose();

        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_result_with_an_unknown_req_id_is_ignored()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a");
        var changes = 0;
        hub.Changed += (_, _) => changes++;

        hub.Accept(a, new ResultMessage("r-unknown", true, null, null, null, null, false, false));

        Assert.Equal(0, changes);
        Assert.Single(hub.Current.Instances);
    }

    [Fact]
    public async Task A_command_over_the_line_cap_is_never_sent_and_answers_with_an_error()
    {
        var hub = Create();
        var (_, connection) = Connect(hub, "id-a");
        var values = Enumerable.Range(0, 40).Select(i => new SkuaOptionValue("Options", "Name" + i, new string('v', 190))).ToList();

        var result = await hub.SendCommandAsync("id-a", new SetOptionsCommand("", "abc", values), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.Ok);
        Assert.Equal(SkuaHub.CommandTooLargeError, result.Error);
        Assert.Empty(connection.Sent);
    }

    [Fact]
    public async Task A_send_that_fails_completes_with_null()
    {
        var hub = Create();
        var (_, connection) = Connect(hub, "id-a");
        connection.Fail = true;

        Assert.Null(await hub.SendCommandAsync("id-a", new StopCommand(""), CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_caller_gets_an_operation_cancelled_exception()
    {
        var hub = Create();
        Connect(hub, "id-a");
        using var cts = new CancellationTokenSource();
        var pending = hub.SendCommandAsync("id-a", new StopCommand(""), cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    // ---- logs per character ----

    [Fact]
    public void Each_character_saves_its_log_to_its_own_store()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        var (b, _) = Connect(hub, "id-b", "Zed");

        hub.Accept(a, Log("hero line", T0));
        hub.Accept(b, Log("zed line", T0));

        Assert.Equal(["hero line"], _stores.For("Hero").Stored.Select(e => e.Text));
        Assert.Equal(["zed line"], _stores.For("Zed").Stored.Select(e => e.Text));
    }

    [Fact]
    public void Log_lines_before_the_login_join_the_character_log_and_are_saved_there()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a");
        hub.Accept(a, Log("before login", T0));
        Assert.Empty(_stores.Created);

        hub.Accept(a, Status("Hero"));

        Assert.Equal(["before login"], _stores.For("Hero").Stored.Select(e => e.Text));
        Assert.Equal(["before login"], Only(hub).Log.Select(e => e.Text));
    }

    [Fact]
    public void A_character_that_logs_in_shows_its_saved_log()
    {
        _stores.For("Hero").Stored = [new SkuaLogEntry(T0.AddDays(-1), SkuaLogKind.Bought, "yesterday")];
        var hub = Create();

        Connect(hub, "id-a", "Hero");

        Assert.Equal(["yesterday"], Only(hub).Log.Select(e => e.Text));
    }

    [Fact]
    public void A_character_change_in_the_same_skua_switches_to_the_other_log()
    {
        _stores.For("Zed").Stored = [new SkuaLogEntry(T0.AddDays(-1), SkuaLogKind.Bought, "zed yesterday")];
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        _now = T0;
        hub.Accept(a, Log("hero 1", T0));
        hub.Accept(a, Log("hero 2", T0.AddSeconds(1)));   // inside the save throttle: unsaved yet

        hub.Accept(a, Status("Zed"));

        Assert.Equal("Zed", Only(hub).Label);
        Assert.Equal(["zed yesterday"], Only(hub).Log.Select(e => e.Text));
        Assert.Equal(["hero 2", "hero 1"], _stores.For("Hero").Stored.Select(e => e.Text));
    }

    [Fact]
    public void The_legacy_log_loads_into_the_first_instance_that_logs_in_and_is_then_deleted()
    {
        _stores.Legacy.Stored = [new SkuaLogEntry(T0.AddDays(-1), SkuaLogKind.Bought, "legacy")];
        var hub = Create();
        var (a, _) = Connect(hub, "id-a");
        Assert.Empty(Only(hub).Log);

        hub.Accept(a, Status("Hero"));
        Connect(hub, "id-b", "Zed");

        Assert.Equal(["legacy"], hub.Current.Instances[0].Log.Select(e => e.Text));
        Assert.Equal(["legacy"], _stores.For("Hero").Stored.Select(e => e.Text));
        Assert.Empty(hub.Current.Instances[1].Log);
        Assert.Equal(1, _stores.Legacy.Deletes);
    }

    [Fact]
    public void A_legacy_log_that_cannot_be_read_is_kept_for_a_later_run()
    {
        _stores.Legacy.Stored = [new SkuaLogEntry(T0.AddDays(-1), SkuaLogKind.Bought, "legacy")];
        _stores.Legacy.LoadFails = true;
        var hub = Create();

        Connect(hub, "id-a", "Hero");

        Assert.Empty(Only(hub).Log);
        Assert.Equal(0, _stores.Legacy.Deletes);
        Assert.Equal(["legacy"], _stores.Legacy.Stored.Select(e => e.Text));
    }

    [Fact]
    public void A_legacy_log_whose_merge_cannot_be_saved_is_kept_and_a_later_run_does_not_double_it()
    {
        _stores.Legacy.Stored = [new SkuaLogEntry(T0.AddDays(-1), SkuaLogKind.Bought, "legacy")];
        _stores.For("Hero").SaveFails = true;
        var hub = Create();

        Connect(hub, "id-a", "Hero");

        Assert.Equal(["legacy"], Only(hub).Log.Select(e => e.Text));
        Assert.Equal(0, _stores.Legacy.Deletes);

        // The next run: the per-character log already holds the legacy line (say its save did land after all).
        _stores.For("Hero").SaveFails = false;
        _stores.For("Hero").Stored = [new SkuaLogEntry(T0.AddDays(-1), SkuaLogKind.Bought, "legacy")];
        var next = Create();
        Connect(next, "id-b", "Hero");

        Assert.Equal(["legacy"], Only(next).Log.Select(e => e.Text));
        Assert.Equal(1, _stores.Legacy.Deletes);
    }

    [Fact]
    public void An_empty_or_missing_legacy_log_is_deleted_at_once()
    {
        var hub = Create();

        Connect(hub, "id-a", "Hero");

        Assert.Equal(1, _stores.Legacy.Deletes);
    }

    [Fact]
    public void Flush_saves_every_instance()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        hub.Accept(a, Log("one", T0));
        hub.Accept(a, Log("two", T0.AddSeconds(1)));

        hub.Flush();

        Assert.Equal(2, _stores.For("Hero").Stored.Count);
    }

    [Fact]
    public void A_disconnect_saves_the_log()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        hub.Accept(a, Log("one", T0));
        hub.Accept(a, Log("two", T0.AddSeconds(1)));

        a.Dispose();

        Assert.Equal(2, _stores.For("Hero").Stored.Count);
    }

    // ---- forgetting ----

    [Fact]
    public async Task Forget_deletes_every_log_file_drops_the_instances_and_fails_waiting_commands()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");
        var (b, _) = Connect(hub, "id-b", "Zed");
        hub.Accept(a, Log("hero line", T0));
        var pending = hub.SendCommandAsync("id-b", new StopCommand(""), CancellationToken.None);
        var forgotten = 0;
        hub.Forgotten += (_, _) => forgotten++;

        hub.Quiesce();
        hub.Forget();

        Assert.Empty(hub.Current.Instances);
        Assert.Equal(1, _stores.DeleteAllCalls);
        Assert.Empty(_stores.For("Hero").Stored);
        Assert.Equal(1, forgotten);
        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5)));

        // Lines of the old pipes are not taken; the plugins reconnect and start over as Skua 1.
        hub.Accept(b, Status("Zed"));
        Assert.Empty(hub.Current.Instances);
        Connect(hub, "id-b", "Zed");
        Assert.Equal("Zed", Only(hub).Label);
        Assert.Empty(Only(hub).Log);
    }

    [Fact]
    public void Forget_frees_the_connection_slots()
    {
        var hub = Create();
        for (var i = 0; i < SkuaProtocol.MaxInstances; i++)
        {
            Connect(hub, "id-" + i);
        }

        hub.Forget();

        Assert.NotNull(hub.Attach(new FakeConnection()));
    }

    [Fact]
    public void While_quiesced_messages_are_ignored_and_resume_accepts_them_again()
    {
        var hub = Create();
        var (a, _) = Connect(hub, "id-a", "Hero");

        hub.Quiesce();
        hub.Accept(a, Log("ignored", T0));
        Assert.Empty(Only(hub).Log);

        hub.Resume();
        hub.Accept(a, Log("taken", T0));
        Assert.Equal(["taken"], Only(hub).Log.Select(e => e.Text));
        Assert.Equal("Skua log", hub.Name);
    }

    // ---- file names ----

    [Theory]
    [InlineData("Hero", "skua-log.hero.json")]
    [InlineData("hero", "skua-log.hero.json")]
    [InlineData("Dark-Lord_7", "skua-log.dark-lord_7.json")]
    public void A_plain_character_name_is_the_file_name_in_lower_case(string character, string expected)
    {
        Assert.Equal(expected, SkuaLogFiles.FileName(character));
    }

    [Theory]
    [InlineData("Dark Lord")]
    [InlineData("../../evil")]
    [InlineData("a\\b/c:d*e?f\"g<h>i|j")]
    [InlineData("con")]
    [InlineData("")]
    [InlineData("Ünïcødé")]
    public void Any_other_name_becomes_a_safe_file_name_in_the_same_folder(string character)
    {
        var name = SkuaLogFiles.FileName(character);

        Assert.StartsWith("skua-log.", name);
        Assert.EndsWith(".json", name);
        Assert.Equal(name, Path.GetFileName(name));
        Assert.DoesNotContain("..", name[9..^5]);
        Assert.All(name, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_', $"bad char {c}"));
        Assert.NotEqual("skua-log.json", name);
    }

    [Fact]
    public void Names_that_differ_only_in_replaced_characters_get_different_files()
    {
        Assert.NotEqual(SkuaLogFiles.FileName("Dark Lord"), SkuaLogFiles.FileName("Dark_Lord"));
        Assert.NotEqual(SkuaLogFiles.FileName("Dark Lord"), SkuaLogFiles.FileName("Dark.Lord"));
    }

    [Fact]
    public void A_very_long_name_is_cut()
    {
        Assert.True(SkuaLogFiles.FileName(new string('a', 500)).Length <= 100);
    }

    // ---- doubles ----

    private sealed class FakeConnection : ISkuaConnection
    {
        public List<SkuaMessage> Sent { get; } = [];

        public bool Fail { get; set; }

        public Task SendAsync(SkuaMessage message, CancellationToken ct)
        {
            if (Fail)
            {
                return Task.FromException(new IOException("pipe broken"));
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimer
    {
        private readonly List<TaskCompletionSource> _pending = [];

        public List<TimeSpan> Requested { get; } = [];

        public Task Delay(TimeSpan delay, CancellationToken ct)
        {
            Requested.Add(delay);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            _pending.Add(tcs);
            return tcs.Task;
        }

        public void Fire()
        {
            foreach (var tcs in _pending)
            {
                tcs.TrySetResult();
            }
        }
    }

    internal sealed class FakeStore : ISkuaLogStore
    {
        public List<SkuaLogEntry> Stored { get; set; } = [];

        public int Deletes { get; private set; }

        public bool LoadFails { get; set; }

        public bool SaveFails { get; set; }

        public IReadOnlyList<SkuaLogEntry> Load() => LoadFails ? [] : Stored;

        public void Save(IReadOnlyList<SkuaLogEntry> log) => TrySave(log);

        public bool TryLoad(out IReadOnlyList<SkuaLogEntry> log)
        {
            log = LoadFails ? [] : Stored;
            return !LoadFails;
        }

        public bool TrySave(IReadOnlyList<SkuaLogEntry> log)
        {
            if (SaveFails)
            {
                return false;
            }

            Stored = [.. log];
            return true;
        }

        public void Delete()
        {
            Deletes++;
            Stored = [];
        }
    }

    internal sealed class FakeStores : ISkuaLogStores
    {
        private readonly Dictionary<string, FakeStore> _byCharacter = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Created { get; } = [];

        public FakeStore Legacy { get; } = new();

        public int DeleteAllCalls { get; private set; }

        public FakeStore For(string character)
        {
            if (!_byCharacter.TryGetValue(character, out var store))
            {
                _byCharacter[character] = store = new FakeStore();
            }

            return store;
        }

        public ISkuaLogStore ForCharacter(string character)
        {
            Created.Add(character);
            return For(character);
        }

        ISkuaLogStore ISkuaLogStores.Legacy => Legacy;

        public void DeleteAll()
        {
            DeleteAllCalls++;
            foreach (var store in _byCharacter.Values)
            {
                store.Stored = [];
            }

            Legacy.Stored = [];
        }
    }
}
