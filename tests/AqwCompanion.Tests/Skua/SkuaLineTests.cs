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
    public void Log_matches_its_fixture_both_ways()
    {
        var message = new LogMessage(new SkuaLogEntry(Noon.AddSeconds(5), SkuaLogKind.Sold, "Sold Blood Orb x50 for 2,500 gold"));

        Assert.Equal(Fixture("log-sold.json"), SkuaLine.ToLine(message));
        Assert.Equal(message, SkuaLine.Parse(Fixture("log-sold.json")));
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
}
