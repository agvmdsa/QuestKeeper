using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaLineTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Skua", "fixtures", name)).Trim();

    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hello_matches_its_fixture_both_ways()
    {
        var message = new HelloMessage(new SkuaHello("0.1.0", "1.4.5.0"));

        Assert.Equal(Fixture("hello.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("hello.json")));
    }

    [Fact]
    public void Status_matches_its_fixture_both_ways()
    {
        var message = new StatusMessage(new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon));

        Assert.Equal(Fixture("status.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("status.json")));
    }

    [Fact]
    public void A_status_with_quests_matches_its_fixture_both_ways()
    {
        var message = new StatusMessage(new SkuaStatus(
            true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon, [new SkuaQuest(5, "Slay the Beast", true), new SkuaQuest(12, "Gather 10 Bones", false)]));

        Assert.Equal(Fixture("status-with-quests.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("status-with-quests.json")));
    }

    [Fact]
    public void A_status_without_quests_leaves_the_field_out_and_parses_back_to_null()
    {
        var line = SkuaLine.ToLine(new StatusMessage(new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon)));

        Assert.DoesNotContain("quests", line);
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(Fixture("status.json")));
        Assert.Null(parsed.Status.Quests);
    }

    [Fact]
    public void No_active_quests_travel_as_an_empty_list_not_as_absent()
    {
        var message = new StatusMessage(new SkuaStatus(true, "Hero", null, null, null, false, null, []));

        var line = SkuaLine.ToLine(message);
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.Contains("\"quests\":[]", line);
        Assert.NotNull(parsed.Status.Quests);
        Assert.Empty(parsed.Status.Quests);
        Assert.Equal(message, parsed);
        Assert.NotEqual(message, message with { Status = message.Status with { Quests = null } });
    }

    [Fact]
    public void Statuses_compare_their_quest_lists_by_content()
    {
        var a = new SkuaStatus(true, "Hero", null, null, null, false, null, [new SkuaQuest(5, "A", false)]);
        var same = a with { Quests = [new SkuaQuest(5, "A", false)] };
        var ready = a with { Quests = [new SkuaQuest(5, "A", true)] };

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, ready);
        Assert.NotEqual(a, a with { Quests = [new SkuaQuest(5, "A", false), new SkuaQuest(6, "B", false)] });
    }

    [Fact]
    public void At_most_thirty_quests_are_written_and_read()
    {
        var quests = Enumerable.Range(1, 40).Select(i => new SkuaQuest(i, $"Q{i}", false)).ToList();
        var message = new StatusMessage(new SkuaStatus(true, "Hero", null, null, null, false, null, quests));

        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(SkuaLine.ToLine(message)));
        Assert.Equal(SkuaProtocol.MaxQuests, parsed.Status.Quests!.Count);
        Assert.Equal(30, parsed.Status.Quests[^1].Id);

        var entries = string.Join(',', Enumerable.Range(1, 40).Select(i => $"{{\"id\":{i},\"name\":\"Q{i}\",\"ready\":true}}"));
        var read = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine($"[{entries}]")));
        Assert.Equal(SkuaProtocol.MaxQuests, read.Status.Quests!.Count);
    }

    [Fact]
    public void A_long_quest_name_is_cut_without_splitting_a_surrogate_pair()
    {
        var name = new string('a', SkuaProtocol.MaxTextLength - 1) + "\U0001F600" + "tail";
        var message = new StatusMessage(new SkuaStatus(true, "Hero", null, null, null, false, null, [new SkuaQuest(1, name, true)]));

        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(SkuaLine.ToLine(message)));
        var cut = parsed.Status.Quests!.Single().Name;

        Assert.True(cut.Length <= SkuaProtocol.MaxTextLength);
        Assert.False(char.IsHighSurrogate(cut[^1]));

        var read = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine($"[{{\"id\":1,\"name\":\"{new string('n', 400)}\",\"ready\":false}}]")));
        Assert.Equal(SkuaProtocol.MaxTextLength, read.Status.Quests!.Single().Name.Length);
    }

    [Fact]
    public void Thirty_long_non_ascii_names_still_fit_the_line_cap_by_leaving_the_last_quests_out()
    {
        var quests = Enumerable.Range(1, 30).Select(i => new SkuaQuest(i, new string('\u00e9', SkuaProtocol.MaxTextLength), true)).ToList();
        var message = new StatusMessage(new SkuaStatus(true, "Hero", null, null, null, false, null, quests));

        var line = SkuaLine.ToLine(message);
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxLineBytes);
        Assert.InRange(parsed.Status.Quests!.Count, 1, 29);
        Assert.Equal(Enumerable.Range(1, parsed.Status.Quests.Count), parsed.Status.Quests.Select(q => q.Id));
    }

    [Theory]
    [InlineData("{\"id\":-1,\"name\":\"neg\",\"ready\":true}")]
    [InlineData("{\"id\":1.5,\"name\":\"frac\",\"ready\":true}")]
    [InlineData("{\"id\":\"7\",\"name\":\"text id\",\"ready\":true}")]
    [InlineData("{\"id\":99999999999,\"name\":\"big\",\"ready\":true}")]
    [InlineData("{\"name\":\"no id\",\"ready\":true}")]
    [InlineData("{\"id\":7,\"ready\":true}")]
    [InlineData("{\"id\":7,\"name\":null,\"ready\":true}")]
    [InlineData("{\"id\":7,\"name\":\"bad \\ud800 name\",\"ready\":true}")]
    [InlineData("{\"id\":7,\"name\":\"ready text\",\"ready\":\"true\"}")]
    [InlineData("{\"id\":7,\"name\":\"no ready\"}")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("[1]")]
    public void A_malformed_quest_is_dropped_and_the_rest_of_the_status_kept(string bad)
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine($"[{bad},{{\"id\":5,\"name\":\"Slay the Beast\",\"ready\":true}}]")));

        Assert.Equal("Hero", parsed.Status.Character);
        Assert.Equal([new SkuaQuest(5, "Slay the Beast", true)], parsed.Status.Quests);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"quests\"")]
    [InlineData("5")]
    [InlineData("true")]
    public void A_quests_field_that_is_not_an_array_counts_as_absent(string value)
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(StatusLine(value)));

        Assert.Equal("Hero", parsed.Status.Character);
        Assert.Null(parsed.Status.Quests);
    }

    // ---- Factions (AQW-0081) ------------------------------------------------------------------------------------

    private static SkuaStatus Hero(IReadOnlyList<SkuaFaction>? factions) => new(true, "Hero", null, null, null, false, null, null, factions);

    [Fact]
    public void A_status_with_factions_matches_its_fixture_both_ways()
    {
        var message = new StatusMessage(new SkuaStatus(
            true, "Hero", "Artix", "battleon", "Farm.cs", true, Noon, null,
            [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200), new SkuaFaction(20, "Good", 10, 302500, 0, 0)]));

        Assert.Equal(Fixture("status-with-factions.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("status-with-factions.json")));
    }

    [Fact]
    public void A_status_without_factions_leaves_the_field_out_and_parses_back_to_null()
    {
        var line = SkuaLine.ToLine(new StatusMessage(Hero(null)));

        Assert.DoesNotContain("factions", line);
        Assert.Null(Assert.IsType<StatusMessage>(SkuaLine.Parse(line)).Status.Factions);
        Assert.Null(Assert.IsType<StatusMessage>(SkuaLine.Parse(Fixture("status.json"))).Status.Factions);
        Assert.Null(Assert.IsType<StatusMessage>(SkuaLine.Parse(Fixture("status-with-quests.json"))).Status.Factions);
    }

    [Fact]
    public void No_factions_travel_as_an_empty_list_not_as_absent()
    {
        var message = new StatusMessage(Hero([]));

        var line = SkuaLine.ToLine(message);
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.Contains("\"factions\":[]", line);
        Assert.NotNull(parsed.Status.Factions);
        Assert.Empty(parsed.Status.Factions);
        Assert.Equal(message, parsed);
        Assert.NotEqual(message, message with { Status = message.Status with { Factions = null } });
    }

    [Fact]
    public void Statuses_compare_their_faction_lists_by_content()
    {
        var a = Hero([new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)]);
        var same = a with { Factions = [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)] };
        var more = a with { Factions = [new SkuaFaction(1, "Nation", 3, 5010, 310, 1200)] };

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, more);
        Assert.NotEqual(a, a with { Factions = [] });
        Assert.NotEqual(a, a with { Factions = [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200), new SkuaFaction(2, "Evil", 1, 10, 10, 900)] });
        Assert.Equal(Hero(null), Hero(null));
    }

    [Fact]
    public void Quests_and_factions_both_travel_in_one_status()
    {
        var message = new StatusMessage(new SkuaStatus(
            true, "Hero", null, null, null, false, null, [new SkuaQuest(5, "Slay", true)], [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)]));

        Assert.Equal(message, SkuaLine.Parse(SkuaLine.ToLine(message)));
    }

    [Fact]
    public void At_most_sixty_factions_are_written_and_read()
    {
        Assert.Equal(60, SkuaProtocol.MaxFactions);
        var factions = Enumerable.Range(1, 70).Select(i => new SkuaFaction(i, $"F{i}", 1, i, i, 900)).ToList();

        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(SkuaLine.ToLine(new StatusMessage(Hero(factions)))));
        Assert.Equal(SkuaProtocol.MaxFactions, parsed.Status.Factions!.Count);
        Assert.Equal(60, parsed.Status.Factions[^1].Id);

        var entries = string.Join(',', Enumerable.Range(1, 70).Select(i => FactionJson($"\"id\":{i},\"name\":\"F{i}\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":900")));
        var read = Assert.IsType<StatusMessage>(SkuaLine.Parse(FactionsLine($"[{entries}]")));
        Assert.Equal(SkuaProtocol.MaxFactions, read.Status.Factions!.Count);
    }

    [Fact]
    public void A_long_faction_name_is_cut_without_splitting_a_surrogate_pair()
    {
        var name = new string('a', SkuaProtocol.MaxTextLength - 1) + "\U0001F600" + "tail";

        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(SkuaLine.ToLine(new StatusMessage(Hero([new SkuaFaction(1, name, 1, 1, 1, 1)])))));
        var cut = parsed.Status.Factions!.Single().Name;

        Assert.True(cut.Length <= SkuaProtocol.MaxTextLength);
        Assert.False(char.IsHighSurrogate(cut[^1]));

        var read = Assert.IsType<StatusMessage>(SkuaLine.Parse(FactionsLine(
            $"[{FactionJson($"\"id\":1,\"name\":\"{new string('n', 400)}\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")}]")));
        Assert.Equal(SkuaProtocol.MaxTextLength, read.Status.Factions!.Single().Name.Length);
    }

    [Fact]
    public void A_lone_surrogate_in_a_faction_name_is_removed_on_the_way_out()
    {
        var lone = ((char)0xD800).ToString();

        var line = SkuaLine.ToLine(new StatusMessage(Hero([new SkuaFaction(1, "Na" + lone + "tion", 1, 1, 1, 1)])));
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.Equal("Nation", parsed.Status.Factions!.Single().Name);
    }

    [Fact]
    public void Sixty_long_non_ascii_faction_names_still_fit_the_line_cap_by_leaving_the_last_factions_out()
    {
        var factions = Enumerable.Range(1, 60).Select(i => new SkuaFaction(i, new string('é', SkuaProtocol.MaxTextLength), 1, 1, 1, 1)).ToList();
        var quests = new List<SkuaQuest> { new(5, "Slay", true) };

        var line = SkuaLine.ToLine(new StatusMessage(Hero(factions) with { Quests = quests }));
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxLineBytes);
        Assert.InRange(parsed.Status.Factions!.Count, 1, 59);
        Assert.Equal(Enumerable.Range(1, parsed.Status.Factions.Count), parsed.Status.Factions.Select(f => f.Id));
        Assert.Equal(quests, parsed.Status.Quests);
    }

    [Theory]
    [InlineData("\"id\":-1,\"name\":\"neg\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"neg rank\",\"rank\":-1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"neg total\",\"rank\":1,\"totalRep\":-1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"neg rep\",\"rank\":1,\"totalRep\":1,\"rep\":-1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"neg required\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":-1")]
    [InlineData("\"id\":1.5,\"name\":\"frac\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"frac rep\",\"rank\":1,\"totalRep\":1,\"rep\":0.5,\"requiredRep\":1")]
    [InlineData("\"id\":\"7\",\"name\":\"text id\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"text rank\",\"rank\":\"1\",\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"big\",\"rank\":1,\"totalRep\":2147483648,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":99999999999,\"name\":\"big id\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"name\":\"no id\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":null,\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"bad \\ud800 name\",\"rank\":1,\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"no rank\",\"totalRep\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"no total\",\"rank\":1,\"rep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"no rep\",\"rank\":1,\"totalRep\":1,\"requiredRep\":1")]
    [InlineData("\"id\":1,\"name\":\"no required\",\"rank\":1,\"totalRep\":1,\"rep\":1")]
    public void A_malformed_faction_is_dropped_and_the_rest_of_the_status_kept(string badFields)
    {
        var good = FactionJson("\"id\":1,\"name\":\"Nation\",\"rank\":3,\"totalRep\":5000,\"rep\":300,\"requiredRep\":1200");

        foreach (var bad in new[] { FactionJson(badFields), "7", "null", "[1]", "\"f\"" })
        {
            var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(FactionsLine($"[{bad},{good}]")));

            Assert.Equal("Hero", parsed.Status.Character);
            Assert.Equal([new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)], parsed.Status.Factions);
        }
    }

    [Fact]
    public void The_largest_int32_values_are_kept()
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(FactionsLine(
            $"[{FactionJson("\"id\":2147483647,\"name\":\"Max\",\"rank\":2147483647,\"totalRep\":2147483647,\"rep\":2147483647,\"requiredRep\":2147483647")}]")));

        Assert.Equal([new SkuaFaction(int.MaxValue, "Max", int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)], parsed.Status.Factions);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"factions\"")]
    [InlineData("5")]
    [InlineData("true")]
    public void A_factions_field_that_is_not_an_array_counts_as_absent(string value)
    {
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(FactionsLine(value)));

        Assert.Equal("Hero", parsed.Status.Character);
        Assert.Null(parsed.Status.Factions);
    }

    private static string FactionJson(string fields) => "{" + fields + "}";

    private static string FactionsLine(string factionsJson) =>
        "{\"v\":1,\"type\":\"status\",\"loggedIn\":true,\"character\":\"Hero\",\"server\":null,\"map\":null,\"script\":null,\"running\":false,\"runningSince\":null,\"factions\":" + factionsJson + "}";

    private static string StatusLine(string questsJson) =>
        "{\"v\":1,\"type\":\"status\",\"loggedIn\":true,\"character\":\"Hero\",\"server\":null,\"map\":null,\"script\":null,\"running\":false,\"runningSince\":null,\"quests\":" + questsJson + "}";

    [Fact]
    public void Log_matches_its_fixture_both_ways()
    {
        var message = new LogMessage(new SkuaLogEntry(Noon.AddSeconds(5), SkuaLogKind.Sold, "Sold Blood Orb x50 for 2,500 gold"));

        Assert.Equal(Fixture("log-sold.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("log-sold.json")));
    }

    [Fact]
    public void A_drop_with_item_and_count_matches_its_fixture_both_ways()
    {
        var message = new LogMessage(new SkuaLogEntry(Noon.AddSeconds(5), SkuaLogKind.Dropped, "Drop: Blood Orb x13", "Blood Orb", 13));

        Assert.Equal(Fixture("log-drop.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("log-drop.json")));
    }

    [Fact]
    public void A_log_line_without_an_item_omits_item_and_count_and_older_lines_parse_with_none()
    {
        var line = SkuaLine.ToLine(new LogMessage(new SkuaLogEntry(Noon, SkuaLogKind.Died, "Died", null, 7)));

        Assert.DoesNotContain("\"item\"", line);
        Assert.DoesNotContain("\"count\"", line);
        var parsed = Assert.IsType<LogMessage>(SkuaLine.Parse(Fixture("log-sold.json")));
        Assert.Null(parsed.Entry.Item);
        Assert.Equal(1, parsed.Entry.Count);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("1.5", 1)]
    [InlineData("\"4\"", 1)]
    [InlineData("null", 1)]
    [InlineData("100000", 1)]
    [InlineData("99999", 99999)]
    [InlineData("1", 1)]
    public void A_count_outside_1_to_99999_parses_as_1_and_keeps_the_line(string count, int expected)
    {
        var line = "{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Dropped\",\"text\":\"Drop: X\",\"item\":\"X\",\"count\":" + count + "}";

        var parsed = Assert.IsType<LogMessage>(SkuaLine.Parse(line));

        Assert.Equal("X", parsed.Entry.Item);
        Assert.Equal(expected, parsed.Entry.Count);
    }

    [Theory]
    [InlineData("\"item\":5,\"count\":3")]
    [InlineData("\"item\":null,\"count\":3")]
    [InlineData("\"item\":\"\\ud800\",\"count\":3")]
    [InlineData("\"item\":\"bad \uFFFD\",\"count\":3")]
    [InlineData("\"count\":3")]
    public void An_unusable_item_drops_item_and_count_but_keeps_the_line(string fields)
    {
        var line = "{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Dropped\",\"text\":\"Drop: X\"," + fields + "}";

        var parsed = Assert.IsType<LogMessage>(SkuaLine.Parse(line));

        Assert.Equal("Drop: X", parsed.Entry.Text);
        Assert.Null(parsed.Entry.Item);
        Assert.Equal(1, parsed.Entry.Count);
    }

    [Fact]
    public void A_long_item_is_cut_and_the_count_is_capped_on_the_way_out()
    {
        var entry = new SkuaLogEntry(Noon, SkuaLogKind.Dropped, "Drop: x", new string('i', SkuaProtocol.MaxTextLength + 20), 250000);

        var parsed = Assert.IsType<LogMessage>(SkuaLine.Parse(SkuaLine.ToLine(new LogMessage(entry))));

        Assert.Equal(SkuaProtocol.MaxTextLength, parsed.Entry.Item!.Length);
        Assert.Equal(SkuaProtocol.MaxCount, parsed.Entry.Count);
    }

    [Fact]
    public void A_status_with_nothing_known_round_trips_with_nulls()
    {
        var message = new StatusMessage(new SkuaStatus(false, null, null, null, null, false, null));

        Assert.Equal(message, SkuaLine.Parse(SkuaLine.ToLine(message)));
    }

    [Fact]
    public void A_line_is_one_line_even_when_the_text_has_line_breaks()
    {
        var message = new LogMessage(new SkuaLogEntry(Noon, SkuaLogKind.ScriptError, "bad\r\nthing"));

        var line = SkuaLine.ToLine(message);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"type\":\"nope\"}")]
    [InlineData("{\"v\":1,\"type\":\"hello\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"not a time\",\"kind\":\"Sold\",\"text\":\"x\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Nope\",\"text\":\"x\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Sold\",\"text\":\"\\ud800\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Sold\",\"text\":\"\\udc00\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Sold\",\"text\":\"\ud800\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"Sold, Died\",\"text\":\"x\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\"3\",\"text\":\"x\"}")]
    [InlineData("{\"v\":1,\"type\":\"log\",\"at\":\"2026-11-11T12:00:05.0000000+00:00\",\"kind\":\" Sold\",\"text\":\"x\"}")]
    public void Malformed_or_unknown_lines_parse_to_null(string line)
    {
        Assert.Null(SkuaLine.Parse(line));
    }

    [Fact]
    public void Another_protocol_version_is_reported_not_parsed()
    {
        var message = SkuaLine.Parse("{\"v\":2,\"type\":\"hello\",\"plugin\":\"9\",\"skua\":\"9\"}");

        Assert.Equal(new UnsupportedVersionMessage(2), message);
    }

    [Fact]
    public void A_line_over_the_byte_cap_is_refused()
    {
        var line = "{\"v\":1,\"type\":\"hello\",\"plugin\":\"" + new string('a', SkuaProtocol.MaxLineBytes) + "\",\"skua\":\"1\"}";

        Assert.Null(SkuaLine.Parse(line));
    }

    [Fact]
    public void Long_text_is_cut_to_the_text_cap_on_the_way_out_and_in()
    {
        var entry = new SkuaLogEntry(Noon, SkuaLogKind.MapChanged, new string('m', SkuaProtocol.MaxTextLength + 50));

        var parsed = Assert.IsType<LogMessage>(SkuaLine.Parse(SkuaLine.ToLine(new LogMessage(entry))));

        Assert.Equal(SkuaProtocol.MaxTextLength, parsed.Entry.Text.Length);
    }

    [Fact]
    public void A_surrogate_pair_straddling_the_cap_is_kept_whole()
    {
        var text = new string('a', SkuaProtocol.MaxTextLength - 1) + "\U0001F600";
        var entry = new SkuaLogEntry(Noon, SkuaLogKind.MapChanged, text);

        var parsed = Assert.IsType<LogMessage>(SkuaLine.Parse(SkuaLine.ToLine(new LogMessage(entry))));

        Assert.True(parsed.Entry.Text.Length <= SkuaProtocol.MaxTextLength);
        Assert.False(char.IsHighSurrogate(parsed.Entry.Text[^1]));
    }

    [Fact]
    public void The_line_cap_counts_bytes_not_characters()
    {
        var line = "{\"v\":1,\"type\":\"hello\",\"plugin\":\"" + new string('\u00e9', 8300) + "\",\"skua\":\"1\"}";

        Assert.True(line.Length < SkuaProtocol.MaxLineBytes);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) > SkuaProtocol.MaxLineBytes);
        Assert.Null(SkuaLine.Parse(line));
    }

    [Fact]
    public void A_status_with_lone_surrogates_in_its_text_is_written_and_round_trips_without_them()
    {
        // Built at run time: a lone surrogate in a literal may be coerced by the compiler.
        var lone = ((char)0xD800).ToString();
        var message = new StatusMessage(new SkuaStatus(
            true, "He" + lone + "ro", "Artix", "battle" + lone + "on", "Farm" + lone, false, null, [new SkuaQuest(5, lone + "Slay", false)]));

        var line = SkuaLine.ToLine(message);
        var parsed = Assert.IsType<StatusMessage>(SkuaLine.Parse(line));

        Assert.Equal(new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm", false, null, [new SkuaQuest(5, "Slay", false)]), parsed.Status);
        Assert.DoesNotContain('\uFFFD', line);
    }

    [Fact]
    public void Hello_and_log_with_lone_surrogates_are_written_and_round_trip_without_them()
    {
        var lone = ((char)0xDC00).ToString();

        var hello = Assert.IsType<HelloMessage>(SkuaLine.Parse(SkuaLine.ToLine(new HelloMessage(new SkuaHello("0.1.0" + lone, "1.4.5.0")))));
        var log = Assert.IsType<LogMessage>(SkuaLine.Parse(SkuaLine.ToLine(
            new LogMessage(new SkuaLogEntry(Noon, SkuaLogKind.Dropped, "Bone" + lone + " x2")))));

        Assert.Equal(new SkuaHello("0.1.0", "1.4.5.0"), hello.Hello);
        Assert.Equal("Bone x2", log.Entry.Text);
    }

    [Fact]
    public void The_shared_log_kinds_fixture_lists_every_kind_the_page_must_place()
    {
        // web/test/skua.test.ts reads the same file and checks that every kind has a filter (or is explicitly in none).
        var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(Fixture("log-kinds.json"));

        Assert.Equal(Enum.GetNames<SkuaLogKind>(), names);
    }
}
