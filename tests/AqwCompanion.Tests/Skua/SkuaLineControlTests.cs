using System.Text;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

/// <summary>Instance ids, control state, commands and results (AQW-0086).</summary>
public class SkuaLineControlTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Skua", "fixtures", name)).Trim();

    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private const string ScriptId = "0a1b2c3d4e5f6071";

    private static void BothWays(string fixture, SkuaMessage message)
    {
        Assert.Equal(Fixture(fixture), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture(fixture)));
    }

    private static ResultMessage Result(
        string reqId = "r1", bool ok = true, string? error = null, IReadOnlyList<SkuaScriptInfo>? scripts = null,
        IReadOnlyList<SkuaOptionDef>? defs = null, IReadOnlyList<SkuaOptionValue>? values = null, bool hasOptions = false, bool firstRun = false,
        bool truncated = false) =>
        new(reqId, ok, error, scripts, defs, values, hasOptions, firstRun, truncated);

    private static T RoundTrip<T>(SkuaMessage message) where T : SkuaMessage => Assert.IsType<T>(SkuaLine.Parse(SkuaLine.ToLine(message)));

    [Fact]
    public void Script_descriptions_match_their_fixture_both_ways()
    {
        BothWays("result-scripts-described.json", Result(scripts:
        [
            new SkuaScriptInfo(ScriptId, "0RunAll", "Hollowborn/HollowbornChaosEnvoy", "Hollowborn Chaos Envy Run all", "does all of hollowborn chaos envoy", "hollowborn, do all"),
            new SkuaScriptInfo("1122334455667788", "Farm", "", "Only a title"),
        ]));
    }

    [Fact]
    public void Missing_title_description_and_tags_are_omitted_not_written_as_null()
    {
        var line = SkuaLine.ToLine(Result(scripts: [new SkuaScriptInfo("1", "A", "F")]));

        Assert.DoesNotContain("title", line);
        Assert.DoesNotContain("description", line);
        Assert.DoesNotContain("tags", line);
    }

    [Fact]
    public void A_script_field_that_is_not_a_string_degrades_that_field_and_keeps_the_entry()
    {
        var line = ResultLine("\"scripts\":[{\"id\":\"9\",\"name\":\"Farm\",\"folder\":\"\",\"title\":5,\"description\":{\"a\":1},\"tags\":\"ok\"},"
            + "{\"id\":\"8\",\"name\":\"B\",\"folder\":\"\",\"title\":null,\"description\":[],\"tags\":true}]");

        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.Equal([new SkuaScriptInfo("9", "Farm", "", null, null, "ok"), new SkuaScriptInfo("8", "B", "")], parsed.Scripts);
    }

    [Fact]
    public void Script_texts_are_cut_to_their_caps_and_lone_surrogates_are_removed_both_ways()
    {
        var lone = "\uD800";
        var scripts = new List<SkuaScriptInfo>
        {
            new("1", "A", "", new string('t', 150) + lone, new string('d', 300), new string('g', 300)),
        };

        var parsed = RoundTrip<ResultMessage>(Result(scripts: scripts));

        var script = Assert.Single(parsed.Scripts!);
        Assert.Equal(SkuaProtocol.MaxScriptTitleLength, script.Title!.Length);
        Assert.Equal(SkuaProtocol.MaxScriptDescriptionLength, script.Description!.Length);
        Assert.Equal(SkuaProtocol.MaxScriptTagsLength, script.Tags!.Length);

        var hostile = ResultLine("\"scripts\":[{\"id\":\"1\",\"name\":\"A\",\"folder\":\"\",\"title\":\"" + new string('t', 500) + "\"}]");
        Assert.Equal(SkuaProtocol.MaxScriptTitleLength, Assert.IsType<ResultMessage>(SkuaLine.Parse(hostile)).Scripts![0].Title!.Length);
    }

    [Fact]
    public void A_full_list_of_2000_described_scripts_fits_one_result_line_under_the_cap()
    {
        var scripts = Enumerable.Range(1, SkuaProtocol.MaxScripts)
            .Select(i => new SkuaScriptInfo($"{i:x16}", $"Some fairly long script name {i}", "Other/Some folder",
                new string('T', 60), new string('D', 150), new string('G', 80))).ToList();

        var line = SkuaLine.ToLine(Result(scripts: scripts));
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.True(Encoding.UTF8.GetByteCount(line) > 256 * 1024);
        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxResultLineBytes);
        Assert.Equal(scripts, parsed.Scripts);
        Assert.False(parsed.Truncated);
    }

    [Fact]
    public void Described_scripts_too_large_for_the_result_cap_lose_the_last_scripts_first()
    {
        var wide = new string('\u00e9', SkuaProtocol.MaxTextLength);
        var scripts = Enumerable.Range(1, SkuaProtocol.MaxScripts)
            .Select(i => new SkuaScriptInfo($"{i:x16}", wide, wide, wide[..SkuaProtocol.MaxScriptTitleLength], wide[..SkuaProtocol.MaxScriptDescriptionLength], wide[..SkuaProtocol.MaxScriptTagsLength])).ToList();

        var line = SkuaLine.ToLine(Result(scripts: scripts));
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxResultLineBytes);
        Assert.InRange(parsed.Scripts!.Count, 1, SkuaProtocol.MaxScripts - 1);
        Assert.Equal(scripts.Take(parsed.Scripts.Count), parsed.Scripts);
        Assert.True(parsed.Truncated);
    }

    private static string ResultLine(string fields) => "{\"v\":1,\"type\":\"result\",\"reqId\":\"r1\",\"ok\":true," + fields + "}";

    // ---- Protocol constants ------------------------------------------------------------------------------------

    [Fact]
    public void The_new_caps_have_their_agreed_values()
    {
        Assert.Equal(2000, SkuaProtocol.MaxScripts);
        Assert.Equal(1024 * 1024, SkuaProtocol.MaxResultLineBytes);
        Assert.Equal(16 * 1024, SkuaProtocol.MaxLineBytes);
        Assert.Equal(200, SkuaProtocol.MaxOptionValueLength);
        Assert.Equal(4 * 1024, SkuaProtocol.MaxCommandLineBytes);
        Assert.Equal(8, SkuaProtocol.MaxInstances);
        Assert.Equal(1, SkuaProtocol.Version);
    }

    // ---- Hello ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_hello_with_an_instance_id_matches_its_fixture_both_ways()
    {
        BothWays("hello-with-instance.json", new HelloMessage(new SkuaHello("0.5.0", "1.4.5.0", "6f1c2a9e-4b7d-4e0a-9d3c-2b8e5f7a1c40")));
    }

    [Fact]
    public void A_hello_without_an_instance_id_leaves_the_field_out_and_an_older_hello_parses_with_none()
    {
        var line = SkuaLine.ToLine(new HelloMessage(new SkuaHello("0.1.0", "1.4.5.0")));

        Assert.Equal(Fixture("hello.json"), line);
        Assert.DoesNotContain("instanceId", line);
        Assert.Null(Assert.IsType<HelloMessage>(SkuaLine.Parse(Fixture("hello.json"))).Hello.InstanceId);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"bad \uFFFD\"")]
    [InlineData("\"\\ud800\"")]
    public void An_unusable_instance_id_is_lost_but_the_hello_kept(string value)
    {
        var parsed = Assert.IsType<HelloMessage>(SkuaLine.Parse("{\"v\":1,\"type\":\"hello\",\"plugin\":\"0.5.0\",\"skua\":\"1.4.5.0\",\"instanceId\":" + value + "}"));

        Assert.Equal(new SkuaHello("0.5.0", "1.4.5.0"), parsed.Hello);
    }

    [Fact]
    public void A_long_instance_id_is_cut_and_a_lone_surrogate_removed()
    {
        var lone = ((char)0xD800).ToString();

        Assert.Equal(SkuaProtocol.MaxTextLength, RoundTrip<HelloMessage>(new HelloMessage(new SkuaHello("1", "1", new string('i', 400)))).Hello.InstanceId!.Length);
        Assert.Equal("abc", RoundTrip<HelloMessage>(new HelloMessage(new SkuaHello("1", "1", "a" + lone + "bc"))).Hello.InstanceId);
    }

    // ---- Status control ------------------------------------------------------------------------------------------

    [Fact]
    public void A_settling_status_matches_its_fixture_both_ways()
    {
        BothWays("status-with-control.json", new StatusMessage(new SkuaStatus(
            true, "Hero", "Artix", "battleon", null, false, null, Control: "settling", SettleSecondsLeft: 7)));
    }

    [Fact]
    public void A_status_with_the_options_window_stuck_matches_its_fixture_both_ways()
    {
        BothWays("status-options-stuck.json", new StatusMessage(new SkuaStatus(
            true, "Hero", "Artix", "battleon", "Butler.cs", true, Noon, Control: "awaitingOptions", OptionsStuck: true)));
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("running")]
    [InlineData("stopping")]
    [InlineData("settling")]
    [InlineData("awaitingOptions")]
    public void Every_control_state_round_trips(string control)
    {
        var status = new SkuaStatus(true, "Hero", null, null, null, false, null, Control: control);

        Assert.Equal(control, RoundTrip<StatusMessage>(new StatusMessage(status)).Status.Control);
    }

    [Theory]
    [InlineData("\"paused\"")]
    [InlineData("\"Idle\"")]
    [InlineData("\" idle\"")]
    [InlineData("\"\"")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"\\ud800\"")]
    public void An_unknown_control_value_parses_to_null_and_keeps_the_status(string value)
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine("\"control\":" + value)));

        Assert.Equal("Hero", parsed.Status.Character);
        Assert.Null(parsed.Status.Control);
    }

    [Fact]
    public void An_unknown_control_value_is_not_written()
    {
        var line = SkuaLine.ToLine(new StatusMessage(new SkuaStatus(true, "Hero", null, null, null, false, null, Control: "paused")));

        Assert.DoesNotContain("control", line);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"7\"")]
    [InlineData("null")]
    [InlineData("99999999999")]
    public void An_unusable_settle_countdown_parses_to_null(string value)
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine("\"control\":\"settling\",\"settleSecondsLeft\":" + value)));

        Assert.Equal("settling", parsed.Status.Control);
        Assert.Null(parsed.Status.SettleSecondsLeft);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("false")]
    public void Options_stuck_is_true_only_for_a_json_true(string value)
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine("\"optionsStuck\":" + value)));

        Assert.False(parsed.Status.OptionsStuck);
        Assert.True(Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine("\"optionsStuck\":true"))).Status.OptionsStuck);
    }

    [Fact]
    public void A_status_from_an_older_plugin_has_no_control_and_writes_none_back()
    {
        foreach (var fixture in new[] { "status.json", "status-with-quests.json", "status-with-factions.json" })
        {
            var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(Fixture(fixture)));

            Assert.Null(parsed.Status.Control);
            Assert.Null(parsed.Status.SettleSecondsLeft);
            Assert.False(parsed.Status.OptionsStuck);
            Assert.Equal(Fixture(fixture), SkuaLine.ToLine(parsed));
        }
    }

    [Fact]
    public void Statuses_compare_their_control_fields()
    {
        var a = new SkuaStatus(true, "Hero", null, null, null, false, null, Control: "settling", SettleSecondsLeft: 5);

        Assert.Equal(a, a with { });
        Assert.Equal(a.GetHashCode(), (a with { }).GetHashCode());
        Assert.NotEqual(a, a with { Control = "idle" });
        Assert.NotEqual(a, a with { SettleSecondsLeft = 4 });
        Assert.NotEqual(a, a with { SettleSecondsLeft = null });
        Assert.NotEqual(a, a with { OptionsStuck = true });
        Assert.NotEqual(a.GetHashCode(), (a with { SettleSecondsLeft = 4 }).GetHashCode());
    }

    [Fact]
    public void The_line_cap_still_holds_for_a_full_status_with_control_and_keeps_the_control()
    {
        var quests = Enumerable.Range(1, 30).Select(i => new SkuaQuest(i, new string('\u00e9', SkuaProtocol.MaxTextLength), true)).ToList();
        var factions = Enumerable.Range(1, 60).Select(i => new SkuaFaction(i, new string('\u00e9', SkuaProtocol.MaxTextLength), 1, 1, 1, 1)).ToList();
        var status = new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", false, null, quests, factions, "settling", 9, true);

        var line = SkuaLine.ToLine(new StatusMessage(status));
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxLineBytes);
        Assert.Equal("settling", parsed.Status.Control);
        Assert.Equal(9, parsed.Status.SettleSecondsLeft);
        Assert.True(parsed.Status.OptionsStuck);
    }

    private static string StatusLine(string extra) =>
        "{\"v\":1,\"type\":\"status\",\"loggedIn\":true,\"character\":\"Hero\",\"server\":null,\"map\":null,\"script\":null,\"running\":false,\"runningSince\":null," + extra + "}";

    // ---- Commands ------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_command_matches_its_fixture_both_ways()
    {
        BothWays("command-list-scripts.json", new ListScriptsCommand("r1"));
        BothWays("command-get-options.json", new GetOptionsCommand("r2", ScriptId));
        BothWays("command-set-options.json", new SetOptionsCommand("r3", ScriptId,
            [new SkuaOptionValue("Options", "PlayerName", "Artix"), new SkuaOptionValue("Core", "Rest", "True")]));
        BothWays("command-start.json", new StartCommand("r4", ScriptId));
        BothWays("command-stop.json", new StopCommand("r5"));
    }

    [Fact]
    public void Every_command_is_a_skua_command_with_a_request_id()
    {
        SkuaCommand[] commands = [new ListScriptsCommand("a"), new GetOptionsCommand("b", ScriptId), new SetOptionsCommand("c", ScriptId, []), new StartCommand("d", ScriptId), new StopCommand("e")];

        Assert.Equal(["a", "b", "c", "d", "e"], commands.Select(c => c.ReqId));
        Assert.All(commands, c => Assert.IsAssignableFrom<SkuaMessage>(c));
    }

    [Fact]
    public void Set_options_with_no_values_travels_as_an_empty_list()
    {
        var parsed = RoundTrip<SetOptionsCommand>(new SetOptionsCommand("r", ScriptId, []));

        Assert.Empty(parsed.Values);
    }

    [Fact]
    public void Set_options_commands_compare_their_values_by_content()
    {
        var a = new SetOptionsCommand("r", ScriptId, [new SkuaOptionValue("Options", "A", "1")]);
        var same = new SetOptionsCommand("r", ScriptId, [new SkuaOptionValue("Options", "A", "1")]);

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, a with { Values = [new SkuaOptionValue("Options", "A", "2")] });
        Assert.NotEqual(a, a with { ReqId = "other" });
        Assert.NotEqual(a, a with { ScriptId = "other" });
    }

    [Theory]
    [InlineData("{\"v\":1,\"type\":\"listScripts\"}")]
    [InlineData("{\"v\":1,\"type\":\"listScripts\",\"reqId\":5}")]
    [InlineData("{\"v\":1,\"type\":\"listScripts\",\"reqId\":\"\"}")]
    [InlineData("{\"v\":1,\"type\":\"listScripts\",\"reqId\":null}")]
    [InlineData("{\"v\":1,\"type\":\"listScripts\",\"reqId\":\"\\ud800\"}")]
    [InlineData("{\"v\":1,\"type\":\"listScripts\",\"reqId\":\"bad \uFFFD\"}")]
    [InlineData("{\"v\":1,\"type\":\"stop\"}")]
    [InlineData("{\"v\":1,\"type\":\"start\",\"reqId\":\"r\"}")]
    [InlineData("{\"v\":1,\"type\":\"start\",\"reqId\":\"r\",\"id\":\"\"}")]
    [InlineData("{\"v\":1,\"type\":\"start\",\"reqId\":\"r\",\"id\":7}")]
    [InlineData("{\"v\":1,\"type\":\"start\",\"reqId\":\"r\",\"id\":\"\\udc00\"}")]
    [InlineData("{\"v\":1,\"type\":\"start\",\"reqId\":\"r\",\"id\":\"x\uFFFD\"}")]
    [InlineData("{\"v\":1,\"type\":\"getOptions\",\"reqId\":\"r\"}")]
    [InlineData("{\"v\":1,\"type\":\"setOptions\",\"reqId\":\"r\",\"values\":[]}")]
    [InlineData("{\"v\":1,\"type\":\"setOptions\",\"reqId\":\"r\",\"id\":\"x\"}")]
    [InlineData("{\"v\":1,\"type\":\"setOptions\",\"reqId\":\"r\",\"id\":\"x\",\"values\":null}")]
    [InlineData("{\"v\":1,\"type\":\"setOptions\",\"reqId\":\"r\",\"id\":\"x\",\"values\":{}}")]
    [InlineData("{\"v\":1,\"type\":\"result\",\"ok\":true}")]
    [InlineData("{\"v\":1,\"type\":\"result\",\"reqId\":\"r\"}")]
    [InlineData("{\"v\":1,\"type\":\"result\",\"reqId\":\"r\",\"ok\":\"true\"}")]
    [InlineData("{\"v\":1,\"type\":\"result\",\"reqId\":\"\",\"ok\":true}")]
    [InlineData("{\"v\":1,\"type\":\"runCode\",\"reqId\":\"r\",\"code\":\"x\"}")]
    [InlineData("{\"v\":1,\"type\":\"Start\",\"reqId\":\"r\",\"id\":\"x\"}")]
    public void Malformed_or_unknown_commands_and_results_parse_to_null(string line)
    {
        Assert.Null(SkuaLine.Parse(line));
    }

    [Fact]
    public void Unknown_fields_on_a_command_are_ignored()
    {
        var parsed = SkuaLine.Parse("{\"v\":1,\"type\":\"start\",\"reqId\":\"r4\",\"id\":\"" + ScriptId + "\",\"path\":\"C:\\\\evil.cs\",\"code\":\"x\"}");

        Assert.Equal(new StartCommand("r4", ScriptId), parsed);
    }

    [Fact]
    public void A_command_line_over_four_kilobytes_is_dropped_and_one_at_the_cap_is_read()
    {
        static string Line(int padding) => "{\"v\":1,\"type\":\"start\",\"reqId\":\"r\",\"id\":\"x\",\"pad\":\"" + new string('p', padding) + "\"}";
        var shell = Encoding.UTF8.GetByteCount(Line(0));

        Assert.Equal(new StartCommand("r", "x"), SkuaLine.Parse(Line(SkuaProtocol.MaxCommandLineBytes - shell)));
        Assert.Null(SkuaLine.Parse(Line(SkuaProtocol.MaxCommandLineBytes - shell + 1)));
    }

    [Fact]
    public void The_command_cap_counts_bytes_and_applies_to_every_command_type()
    {
        var pad = new string('\u00e9', 2100);

        Assert.True(("\"pad\":\"" + pad + "\"").Length < SkuaProtocol.MaxCommandLineBytes);
        foreach (var head in new[] { "\"type\":\"listScripts\",\"reqId\":\"r\"", "\"type\":\"getOptions\",\"reqId\":\"r\",\"id\":\"x\"",
                     "\"type\":\"setOptions\",\"reqId\":\"r\",\"id\":\"x\",\"values\":[]", "\"type\":\"start\",\"reqId\":\"r\",\"id\":\"x\"", "\"type\":\"stop\",\"reqId\":\"r\"" })
        {
            Assert.NotNull(SkuaLine.Parse("{\"v\":1," + head + "}"));
            Assert.Null(SkuaLine.Parse("{\"v\":1," + head + ",\"pad\":\"" + pad + "\"}"));
        }
    }

    [Fact]
    public void Results_and_statuses_are_not_held_to_the_command_cap()
    {
        var scripts = Enumerable.Range(1, 100).Select(i => new SkuaScriptInfo($"{i:x16}", $"Script{i}.cs", "Farm")).ToList();
        var line = SkuaLine.ToLine(Result(scripts: scripts));

        Assert.True(Encoding.UTF8.GetByteCount(line) > SkuaProtocol.MaxCommandLineBytes);
        Assert.Equal(100, Assert.IsType<ResultMessage>(SkuaLine.Parse(line)).Scripts!.Count);
    }

    [Theory]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"value\":5}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"value\":null}")]
    [InlineData("{\"category\":\"Options\",\"value\":\"x\"}")]
    [InlineData("{\"name\":\"A\",\"value\":\"x\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"\",\"value\":\"x\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"\\ud800\",\"value\":\"x\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"value\":\"bad \uFFFD\"}")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("[1]")]
    public void A_malformed_option_value_is_dropped_and_the_rest_of_the_command_kept(string bad)
    {
        var parsed = Assert.IsType<SetOptionsCommand>(SkuaLine.Parse(
            "{\"v\":1,\"type\":\"setOptions\",\"reqId\":\"r\",\"id\":\"x\",\"values\":[" + bad + ",{\"category\":\"Options\",\"name\":\"B\",\"value\":\"ok\"}]}"));

        Assert.Equal([new SkuaOptionValue("Options", "B", "ok")], parsed.Values);
    }

    [Fact]
    public void An_option_value_over_200_characters_is_dropped_on_its_own_both_ways()
    {
        var max = new string('v', SkuaProtocol.MaxOptionValueLength);
        var over = max + "v";
        var values = new List<SkuaOptionValue> { new("Options", "A", over), new("Options", "B", max), new("Options", "C", "") };

        var parsed = RoundTrip<SetOptionsCommand>(new SetOptionsCommand("r", "x", values));
        Assert.Equal([values[1], values[2]], parsed.Values);

        var read = Assert.IsType<SetOptionsCommand>(SkuaLine.Parse(
            "{\"v\":1,\"type\":\"setOptions\",\"reqId\":\"r\",\"id\":\"x\",\"values\":[{\"category\":\"Options\",\"name\":\"A\",\"value\":\"" + over + "\"},{\"category\":\"Options\",\"name\":\"B\",\"value\":\"" + max + "\"}]}"));
        Assert.Equal([values[1]], read.Values);
    }

    [Fact]
    public void The_200_character_value_cap_does_not_split_a_surrogate_pair_count()
    {
        // 100 emoji are 200 UTF-16 chars: exactly at the cap.
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 100));

        Assert.Equal(emoji, RoundTrip<SetOptionsCommand>(new SetOptionsCommand("r", "x", [new SkuaOptionValue("Options", "A", emoji)])).Values.Single().Value);
    }

    [Fact]
    public void Lone_surrogates_in_a_command_are_removed_on_the_way_out()
    {
        var lone = ((char)0xD800).ToString();

        var parsed = RoundTrip<SetOptionsCommand>(new SetOptionsCommand("r" + lone, "x" + lone, [new SkuaOptionValue("Opt" + lone + "ions", "A" + lone, "v" + lone + "al")]));

        Assert.Equal(new SetOptionsCommand("r", "x", [new SkuaOptionValue("Options", "A", "val")]), parsed);
        Assert.DoesNotContain('\uFFFD', SkuaLine.ToLine(parsed));
    }

    [Fact]
    public void Option_values_with_line_breaks_and_equals_signs_stay_on_one_line()
    {
        var value = new SkuaOptionValue("Options", "A", "a=b:c\r\nd");

        var line = SkuaLine.ToLine(new SetOptionsCommand("r", "x", [value]));

        Assert.DoesNotContain('\n', line);
        Assert.Equal([value], Assert.IsType<SetOptionsCommand>(SkuaLine.Parse(line)).Values);
    }

    // ---- Results -------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_result_shape_matches_its_fixture_both_ways()
    {
        BothWays("result-scripts.json", Result(scripts: [new SkuaScriptInfo(ScriptId, "Butler.cs", "Other"), new SkuaScriptInfo("1122334455667788", "Farm.cs", "")]));
        BothWays("result-options.json", Result(
            reqId: "r2",
            defs:
            [
                new SkuaOptionDef("Options", "PlayerName", "Player to follow", "The name of the player to follow", "string", ""),
                new SkuaOptionDef("Options", "Mode", "Mode", "How to follow", "enum", "Near", ["Near", "Far"]),
            ],
            values: [new SkuaOptionValue("Options", "PlayerName", "Artix")],
            hasOptions: true));
        BothWays("result-first-run.json", Result(reqId: "r4", defs: [new SkuaOptionDef("Options", "Rest", "Rest", "", "bool", "False")], hasOptions: true, firstRun: true));
        BothWays("result-error.json", Result(reqId: "r4", ok: false, error: "settling"));
    }

    [Fact]
    public void A_result_with_nothing_optional_leaves_the_lists_and_error_out_and_parses_back_to_null()
    {
        var line = SkuaLine.ToLine(Result());
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.Equal("{\"v\":1,\"type\":\"result\",\"reqId\":\"r1\",\"ok\":true,\"hasOptions\":false,\"firstRun\":false}", line);
        Assert.Null(parsed.Error);
        Assert.Null(parsed.Scripts);
        Assert.Null(parsed.OptionDefs);
        Assert.Null(parsed.Values);
        Assert.Equal(Result(), parsed);
    }

    [Fact]
    public void Empty_lists_in_a_result_travel_as_empty_not_as_absent()
    {
        var message = Result(scripts: [], defs: [], values: []);

        var line = SkuaLine.ToLine(message);
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.Contains("\"scripts\":[]", line);
        Assert.Contains("\"optionDefs\":[]", line);
        Assert.Contains("\"options\":[]", line);
        Assert.Empty(parsed.Scripts!);
        Assert.Empty(parsed.OptionDefs!);
        Assert.Empty(parsed.Values!);
        Assert.Equal(message, parsed);
        Assert.NotEqual(message, Result());
        Assert.NotEqual(message, Result(defs: [], values: []));
    }

    [Fact]
    public void Results_compare_their_lists_by_content()
    {
        var a = Result(scripts: [new SkuaScriptInfo("1", "A.cs", "")], defs: [new SkuaOptionDef("O", "N", "D", "", "enum", "x", ["x", "y"])], values: [new SkuaOptionValue("O", "N", "x")]);
        var same = Result(scripts: [new SkuaScriptInfo("1", "A.cs", "")], defs: [new SkuaOptionDef("O", "N", "D", "", "enum", "x", ["x", "y"])], values: [new SkuaOptionValue("O", "N", "x")]);

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, a with { Scripts = [new SkuaScriptInfo("2", "A.cs", "")] });
        Assert.NotEqual(a, a with { OptionDefs = [new SkuaOptionDef("O", "N", "D", "", "enum", "x", ["x"])] });
        Assert.NotEqual(a, a with { OptionDefs = [new SkuaOptionDef("O", "N", "D", "", "enum", "x")] });
        Assert.NotEqual(a, a with { Values = [new SkuaOptionValue("O", "N", "y")] });
        Assert.NotEqual(a, a with { Ok = false });
        Assert.NotEqual(a, a with { Error = "e" });
        Assert.NotEqual(a, a with { HasOptions = true });
        Assert.NotEqual(a, a with { FirstRun = true });
        Assert.NotEqual(a, a with { ReqId = "r2" });
    }

    [Fact]
    public void Option_defs_compare_their_choices_by_content()
    {
        var a = new SkuaOptionDef("O", "N", "D", "", "enum", "x", ["x", "y"]);

        Assert.Equal(a, new SkuaOptionDef("O", "N", "D", "", "enum", "x", ["x", "y"]));
        Assert.Equal(a.GetHashCode(), new SkuaOptionDef("O", "N", "D", "", "enum", "x", ["x", "y"]).GetHashCode());
        Assert.NotEqual(a, a with { Choices = null });
        Assert.NotEqual(a, a with { Choices = [] });
        Assert.Equal(a with { Choices = null }, new SkuaOptionDef("O", "N", "D", "", "enum", "x"));
    }

    [Fact]
    public void An_error_text_is_cut_to_the_text_cap_without_splitting_a_pair()
    {
        var error = new string('e', SkuaProtocol.MaxTextLength - 1) + "\U0001F600" + "tail";

        var parsed = RoundTrip<ResultMessage>(Result(ok: false, error: error));
        Assert.True(parsed.Error!.Length <= SkuaProtocol.MaxTextLength);
        Assert.False(char.IsHighSurrogate(parsed.Error[^1]));

        var read = Assert.IsType<ResultMessage>(SkuaLine.Parse("{\"v\":1,\"type\":\"result\",\"reqId\":\"r\",\"ok\":false,\"error\":\"" + new string('e', 400) + "\"}"));
        Assert.Equal(SkuaProtocol.MaxTextLength, read.Error!.Length);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("\"bad \uFFFD\"")]
    [InlineData("\"\\ud800\"")]
    public void An_unusable_error_is_lost_but_the_result_kept(string value)
    {
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse("{\"v\":1,\"type\":\"result\",\"reqId\":\"r\",\"ok\":false,\"error\":" + value + "}"));

        Assert.False(parsed.Ok);
        Assert.Null(parsed.Error);
    }

    [Theory]
    [InlineData("\"hasOptions\":\"true\",\"firstRun\":1")]
    [InlineData("\"hasOptions\":null,\"firstRun\":null")]
    [InlineData("")]
    public void Has_options_and_first_run_are_true_only_for_a_json_true(string fields)
    {
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine(fields.Length == 0 ? "\"x\":0" : fields)));

        Assert.False(parsed.HasOptions);
        Assert.False(parsed.FirstRun);
    }

    [Fact]
    public void At_most_2000_scripts_are_written_and_a_cut_list_is_marked_truncated()
    {
        var small = Enumerable.Range(1, 2100).Select(i => new SkuaScriptInfo($"{i}", "s", "")).ToList();

        var written = RoundTrip<ResultMessage>(Result(scripts: small));

        Assert.Equal(SkuaProtocol.MaxScripts, written.Scripts!.Count);
        Assert.Equal(small.Take(SkuaProtocol.MaxScripts), written.Scripts);
        Assert.True(written.Truncated);
    }

    [Fact]
    public void A_full_list_of_2000_realistic_scripts_fits_one_result_line_and_is_not_truncated()
    {
        var scripts = Enumerable.Range(1, SkuaProtocol.MaxScripts)
            .Select(i => new SkuaScriptInfo($"{i:x16}", $"Some fairly long script name {i}.cs", "Other/Some folder")).ToList();

        var line = SkuaLine.ToLine(Result(scripts: scripts));
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.True(Encoding.UTF8.GetByteCount(line) > SkuaProtocol.MaxLineBytes);
        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxResultLineBytes);
        Assert.Equal(scripts, parsed.Scripts);
        Assert.False(parsed.Truncated);
        Assert.DoesNotContain("truncated", line);
    }

    [Fact]
    public void A_script_list_too_large_for_the_result_cap_leaves_the_last_scripts_out_and_says_so()
    {
        var name = new string('\u00e9', SkuaProtocol.MaxTextLength);
        var scripts = Enumerable.Range(1, SkuaProtocol.MaxScripts).Select(i => new SkuaScriptInfo($"{i:x16}", name, "Other")).ToList();

        var line = SkuaLine.ToLine(Result(scripts: scripts, hasOptions: true));
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxResultLineBytes);
        Assert.InRange(parsed.Scripts!.Count, 1, SkuaProtocol.MaxScripts - 1);
        Assert.Equal(scripts.Take(parsed.Scripts.Count), parsed.Scripts);
        Assert.True(parsed.Truncated);
        Assert.True(parsed.Ok);
        Assert.True(parsed.HasOptions);
    }

    [Fact]
    public void A_result_still_too_large_without_scripts_becomes_a_result_too_large_error()
    {
        var text = new string('\u00e9', SkuaProtocol.MaxTextLength);
        var defs = Enumerable.Range(1, 900).Select(i => new SkuaOptionDef("Options", $"N{i}", text, text, "string", "")).ToList();
        var values = Enumerable.Range(1, 900).Select(i => new SkuaOptionValue("Options", $"N{i}", new string('\u00e9', SkuaProtocol.MaxOptionValueLength))).ToList();
        var scripts = new List<SkuaScriptInfo> { new("1", "A.cs", "") };

        var line = SkuaLine.ToLine(Result(reqId: "keep", scripts: scripts, defs: defs, values: values, hasOptions: true, firstRun: true));
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.Equal(new ResultMessage("keep", false, "result too large", null, null, null, false, false), parsed);
    }

    [Fact]
    public void Truncated_matches_its_fixture_both_ways_and_is_omitted_when_false()
    {
        BothWays("result-truncated.json", Result(scripts: [new SkuaScriptInfo(ScriptId, "Butler.cs", "Other")], truncated: true));
        Assert.DoesNotContain("truncated", SkuaLine.ToLine(Result(scripts: [])));
    }

    [Theory]
    [InlineData("\"truncated\":\"true\"", false)]
    [InlineData("\"truncated\":1", false)]
    [InlineData("\"truncated\":null", false)]
    [InlineData("\"truncated\":true", true)]
    public void Truncated_is_true_only_for_a_json_true(string field, bool expected)
    {
        Assert.Equal(expected, Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine(field))).Truncated);
    }

    [Fact]
    public void Results_compare_and_hash_the_truncated_flag()
    {
        var a = Result(scripts: []);

        Assert.NotEqual(a, a with { Truncated = true });
        Assert.NotEqual(a.GetHashCode(), (a with { Truncated = true }).GetHashCode());
    }

    [Fact]
    public void Only_results_may_use_the_larger_line_cap()
    {
        var pad = new string('p', SkuaProtocol.MaxLineBytes);

        Assert.NotNull(SkuaLine.Parse(ResultLine("\"pad\":\"" + pad + "\"")));
        Assert.Null(SkuaLine.Parse(StatusLine("\"pad\":\"" + pad + "\"")));
        Assert.Null(SkuaLine.Parse("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Sold\",\"text\":\"x\",\"pad\":\"" + pad + "\"}"));
        Assert.Null(SkuaLine.Parse("{\"v\":1,\"type\":\"hello\",\"plugin\":\"1\",\"skua\":\"1\",\"pad\":\"" + pad + "\"}"));
        Assert.Null(SkuaLine.Parse("{\"v\":1,\"type\":\"nope\",\"pad\":\"" + pad + "\"}"));
        Assert.Null(SkuaLine.Parse(ResultLine("\"pad\":\"" + new string('p', SkuaProtocol.MaxResultLineBytes) + "\"")));
    }

    [Fact]
    public void A_result_line_at_the_result_cap_is_read_and_one_byte_over_is_refused()
    {
        static string Line(int padding) => ResultLine("\"pad\":\"" + new string('p', padding) + "\"");
        var shell = Encoding.UTF8.GetByteCount(Line(0));

        Assert.NotNull(SkuaLine.Parse(Line(SkuaProtocol.MaxResultLineBytes - shell)));
        Assert.Null(SkuaLine.Parse(Line(SkuaProtocol.MaxResultLineBytes - shell + 1)));
    }

    // ---- Sending a command -------------------------------------------------------------------------------------

    [Fact]
    public void A_command_line_is_the_same_as_its_line_when_it_fits()
    {
        SkuaCommand[] commands = [new ListScriptsCommand("r1"), new GetOptionsCommand("r2", ScriptId), new StartCommand("r4", ScriptId), new StopCommand("r5"),
            new SetOptionsCommand("r3", ScriptId, [new SkuaOptionValue("Options", "PlayerName", "Artix")])];

        Assert.All(commands, c => Assert.Equal(SkuaLine.ToLine(c), SkuaLine.ToCommandLine(c)));
    }

    [Fact]
    public void A_command_too_large_for_the_plugin_gives_no_line_and_one_at_the_cap_is_sent()
    {
        // 15 values of 200 'v' are under 4 KB; escaped non-ASCII (6 bytes a char) makes the same count far larger.
        var fits = Enumerable.Range(1, 15).Select(i => new SkuaOptionValue("Options", $"N{i}", new string('v', SkuaProtocol.MaxOptionValueLength))).ToList();
        var escaped = fits.Select(v => v with { Value = new string('\u00e9', SkuaProtocol.MaxOptionValueLength) }).ToList();

        var line = SkuaLine.ToCommandLine(new SetOptionsCommand("r", ScriptId, fits));
        Assert.NotNull(line);
        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxCommandLineBytes);
        Assert.NotNull(SkuaLine.Parse(line));

        Assert.True(Encoding.UTF8.GetByteCount(SkuaLine.ToLine(new SetOptionsCommand("r", ScriptId, escaped))) > SkuaProtocol.MaxCommandLineBytes);
        Assert.Null(SkuaLine.ToCommandLine(new SetOptionsCommand("r", ScriptId, escaped)));
    }

    [Fact]
    public void Exactly_at_the_command_cap_is_sent()
    {
        var shell = Encoding.UTF8.GetByteCount(SkuaLine.ToLine(new SetOptionsCommand("", "x", [new SkuaOptionValue("O", "N", "")])));
        // Grow the request id so the line lands exactly on the cap (ids are cut to MaxTextLength, values to 200: use many values).
        var values = new List<SkuaOptionValue>();
        var size = shell;
        var entry = Encoding.UTF8.GetByteCount(",{\"category\":\"O\",\"name\":\"N\",\"value\":\"\"}");
        while (size + entry <= SkuaProtocol.MaxCommandLineBytes)
        {
            values.Add(new SkuaOptionValue("O", "N", ""));
            size += entry;
        }

        values.Add(new SkuaOptionValue("O", "N", ""));
        var rest = SkuaProtocol.MaxCommandLineBytes - size;
        Assert.InRange(rest, 0, SkuaProtocol.MaxOptionValueLength);
        values[^1] = values[^1] with { Value = new string('v', rest) };

        var exact = new SetOptionsCommand("", "x", values);
        Assert.Equal(SkuaProtocol.MaxCommandLineBytes, Encoding.UTF8.GetByteCount(SkuaLine.ToLine(exact)));
        Assert.NotNull(SkuaLine.ToCommandLine(exact));

        values[^1] = values[^1] with { Value = new string('v', rest + 1) };
        Assert.Null(SkuaLine.ToCommandLine(new SetOptionsCommand("", "x", values)));
    }

    [Theory]
    [InlineData("{\"name\":\"A.cs\",\"folder\":\"\"}")]
    [InlineData("{\"id\":\"\",\"name\":\"A.cs\",\"folder\":\"\"}")]
    [InlineData("{\"id\":5,\"name\":\"A.cs\",\"folder\":\"\"}")]
    [InlineData("{\"id\":\"1\",\"folder\":\"\"}")]
    [InlineData("{\"id\":\"1\",\"name\":\"A.cs\"}")]
    [InlineData("{\"id\":\"1\",\"name\":null,\"folder\":\"\"}")]
    [InlineData("{\"id\":\"1\",\"name\":\"\\ud800\",\"folder\":\"\"}")]
    [InlineData("{\"id\":\"1\",\"name\":\"A.cs\",\"folder\":\"bad \uFFFD\"}")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("[1]")]
    public void A_malformed_script_is_dropped_and_the_rest_of_the_result_kept(string bad)
    {
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine("\"scripts\":[" + bad + ",{\"id\":\"9\",\"name\":\"Farm.cs\",\"folder\":\"Other\"}]")));

        Assert.Equal([new SkuaScriptInfo("9", "Farm.cs", "Other")], parsed.Scripts);
    }

    [Theory]
    [InlineData("{\"name\":\"A\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"description\":\"\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"description\":\"\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"bool\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"displayName\":5,\"description\":\"\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"\\ud800\",\"description\":\"\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"description\":\"bad \uFFFD\",\"type\":\"bool\",\"default\":\"False\"}")]
    [InlineData("7")]
    [InlineData("null")]
    public void A_malformed_option_def_is_dropped_and_the_rest_of_the_result_kept(string bad)
    {
        var good = "{\"category\":\"Options\",\"name\":\"B\",\"displayName\":\"B\",\"description\":\"d\",\"type\":\"int\",\"default\":\"3\"}";

        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine("\"optionDefs\":[" + bad + "," + good + "]")));

        Assert.Equal([new SkuaOptionDef("Options", "B", "B", "d", "int", "3")], parsed.OptionDefs);
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("int")]
    [InlineData("double")]
    [InlineData("string")]
    [InlineData("enum")]
    [InlineData("other")]
    public void Every_option_type_round_trips(string type)
    {
        var def = new SkuaOptionDef("Options", "A", "A", "", type, "");

        Assert.Equal(type, RoundTrip<ResultMessage>(Result(defs: [def])).OptionDefs!.Single().Type);
    }

    [Theory]
    [InlineData("Bool")]
    [InlineData("float")]
    [InlineData("System.Int32")]
    [InlineData("")]
    public void An_unknown_option_type_becomes_other_both_ways(string type)
    {
        var def = new SkuaOptionDef("Options", "A", "A", "", type, "");
        Assert.Equal("other", RoundTrip<ResultMessage>(Result(defs: [def])).OptionDefs!.Single().Type);

        var read = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine(
            "\"optionDefs\":[{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"" + type + "\",\"default\":\"\"}]")));
        Assert.Equal("other", read.OptionDefs!.Single().Type);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("{}")]
    public void Choices_that_are_not_an_array_count_as_absent(string value)
    {
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine(
            "\"optionDefs\":[{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"enum\",\"default\":\"x\",\"choices\":" + value + "}]")));

        Assert.Null(parsed.OptionDefs!.Single().Choices);
    }

    [Fact]
    public void A_bad_choice_is_dropped_on_its_own_and_an_over_long_choice_too()
    {
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine(
            "\"optionDefs\":[{\"category\":\"Options\",\"name\":\"A\",\"displayName\":\"A\",\"description\":\"\",\"type\":\"enum\",\"default\":\"x\",\"choices\":[\"x\",5,null,\"\\ud800\",\"bad \uFFFD\",\""
            + new string('c', SkuaProtocol.MaxOptionValueLength + 1) + "\",\"y\"]}]")));

        Assert.Equal(["x", "y"], parsed.OptionDefs!.Single().Choices);

        var written = RoundTrip<ResultMessage>(Result(defs: [new SkuaOptionDef("O", "A", "A", "", "enum", "x", ["x", new string('c', SkuaProtocol.MaxOptionValueLength + 1), "y"])]));
        Assert.Equal(["x", "y"], written.OptionDefs!.Single().Choices);
    }

    [Fact]
    public void An_option_def_without_choices_leaves_the_field_out()
    {
        var line = SkuaLine.ToLine(Result(defs: [new SkuaOptionDef("Options", "A", "A", "", "bool", "False")]));

        Assert.DoesNotContain("choices", line);
        Assert.Null(Assert.IsType<ResultMessage>(SkuaLine.Parse(line)).OptionDefs!.Single().Choices);
    }

    [Fact]
    public void Long_option_def_texts_are_cut_and_a_long_default_is_cut_to_the_value_cap()
    {
        var long1 = new string('t', SkuaProtocol.MaxTextLength + 50);
        var def = new SkuaOptionDef("Options", "A", long1, long1, "string", long1);

        var parsed = RoundTrip<ResultMessage>(Result(defs: [def])).OptionDefs!.Single();

        Assert.Equal(SkuaProtocol.MaxTextLength, parsed.DisplayName.Length);
        Assert.Equal(SkuaProtocol.MaxTextLength, parsed.Description.Length);
        Assert.Equal(SkuaProtocol.MaxOptionValueLength, parsed.Default.Length);
    }

    [Fact]
    public void A_result_value_over_200_characters_is_dropped_on_its_own()
    {
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine(
            "\"options\":[{\"category\":\"Options\",\"name\":\"A\",\"value\":\"" + new string('v', SkuaProtocol.MaxOptionValueLength + 1) + "\"},{\"category\":\"Options\",\"name\":\"B\",\"value\":\"ok\"}]")));

        Assert.Equal([new SkuaOptionValue("Options", "B", "ok")], parsed.Values);
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("optionDefs")]
    [InlineData("options")]
    public void A_result_list_that_is_not_an_array_counts_as_absent(string field)
    {
        foreach (var value in new[] { "null", "5", "{}", "\"x\"" })
        {
            var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(ResultLine($"\"{field}\":{value}")));

            Assert.Null(parsed.Scripts);
            Assert.Null(parsed.OptionDefs);
            Assert.Null(parsed.Values);
        }
    }

    [Fact]
    public void Lone_surrogates_in_a_result_are_removed_on_the_way_out()
    {
        var lone = ((char)0xDC00).ToString();
        var message = Result(
            reqId: "r" + lone, ok: false, error: "comp" + lone + "ile",
            scripts: [new SkuaScriptInfo("1" + lone, "A" + lone + ".cs", "F" + lone)],
            defs: [new SkuaOptionDef("O" + lone, "N" + lone, "D" + lone, "d" + lone, "enum", "x" + lone, ["x" + lone])],
            values: [new SkuaOptionValue("O" + lone, "N" + lone, "v" + lone)]);

        var line = SkuaLine.ToLine(message);
        var parsed = Assert.IsType<ResultMessage>(SkuaLine.Parse(line));

        Assert.DoesNotContain('\uFFFD', line);
        Assert.Equal(Result(
            reqId: "r", ok: false, error: "compile",
            scripts: [new SkuaScriptInfo("1", "A.cs", "F")],
            defs: [new SkuaOptionDef("O", "N", "D", "d", "enum", "x", ["x"])],
            values: [new SkuaOptionValue("O", "N", "v")]), parsed);
    }

    [Fact]
    public void Unknown_types_still_parse_to_null_and_an_old_line_kind_is_unchanged()
    {
        Assert.Null(SkuaLine.Parse("{\"v\":1,\"type\":\"command\",\"reqId\":\"r\"}"));
        Assert.Equal(new UnsupportedVersionMessage(2), SkuaLine.Parse("{\"v\":2,\"type\":\"stop\",\"reqId\":\"r\"}"));
    }
}
