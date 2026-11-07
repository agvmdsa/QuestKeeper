using System.Text;
using AqwCompanion.Core.Reputation;

namespace AqwCompanion.Tests.Reputation;

public class ReputationParserTests
{
    private static ReputationParseResult Parse(string json) => ReputationParser.Parse(Encoding.UTF8.GetBytes(json));

    private static string Entry(string id = "1", string name = "\"Good\"", string rank = "5", string rep = "10", string toRank = "20") =>
        $$"""{"factionId":{{id}},"name":{{name}},"rank":{{rank}},"rep":{{rep}},"repToRank":{{toRank}}}""";

    private static string File(params string[] entries) => $$"""{"schema":1,"factions":[{{string.Join(",", entries)}}]}""";

    private static string Q(string text) => "\"" + text + "\"";

    [Fact]
    public void A_valid_file_gives_every_standing_and_the_export_time()
    {
        var result = Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reputation", "Samples", "reputation-sample.json")));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal("2026-11-07T15:00:00Z", result.Snapshot!.ExportedAt);
        Assert.Equal(4, result.Snapshot.Factions.Count);
        Assert.Equal(new FactionStanding(1, "Good", 5, 1234, 5000, 0), result.Snapshot.Factions[0]);
        Assert.Equal(25, result.Snapshot.Factions[1].SpillRep);
        Assert.Equal(0, result.Snapshot.Factions[2].SpillRep);
    }

    [Fact]
    public void An_empty_factions_list_is_an_error()
    {
        var result = Parse(File());

        Assert.False(result.IsSuccess);
        Assert.Equal(ReputationParser.NoValidEntriesError, result.Error);
    }

    [Theory]
    [InlineData("""{"schema":2,"factions":[]}""")]
    [InlineData("""{"schema":"1","factions":[]}""")]
    [InlineData("""{"schema":1.5,"factions":[]}""")]
    [InlineData("""{"factions":[]}""")]
    public void Any_schema_but_1_is_unsupported(string json)
    {
        var result = Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReputationParser.UnsupportedSchemaError, result.Error);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void A_root_that_is_not_an_object_is_an_error(string json) =>
        Assert.Equal(ReputationParser.NotObjectError, Parse(json).Error);

    [Theory]
    [InlineData("")]
    [InlineData("{\"schema\":1,")]
    [InlineData("<html></html>")]
    public void Malformed_JSON_is_an_error(string json) =>
        Assert.Equal(ReputationParser.NotJsonError, Parse(json).Error);

    [Fact]
    public void Deeply_nested_JSON_is_an_error_not_an_exception() =>
        Assert.Equal(ReputationParser.NotJsonError, Parse(new string('[', 100) + new string(']', 100)).Error);

    [Theory]
    [InlineData("""{"schema":1}""")]
    [InlineData("""{"schema":1,"factions":{}}""")]
    public void A_missing_factions_array_is_an_error(string json) =>
        Assert.Equal(ReputationParser.NoFactionsError, Parse(json).Error);

    [Fact]
    public void A_file_over_the_cap_is_rejected_without_being_parsed()
    {
        var bytes = new byte[ReputationParser.MaxBytes + 1];
        Array.Fill(bytes, (byte)' ');

        Assert.Equal(ReputationParser.TooLargeError, ReputationParser.Parse(bytes).Error);
    }

    [Fact]
    public async Task A_stream_that_reports_a_size_over_the_cap_is_not_read()
    {
        var stream = new CountingStream(new byte[ReputationParser.MaxBytes + 10]);

        var result = await ReputationParser.ParseAsync(stream);

        Assert.Equal(ReputationParser.TooLargeError, result.Error);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task A_stream_of_unknown_length_is_read_for_at_most_the_cap_plus_one_byte()
    {
        var stream = new CountingStream(new byte[ReputationParser.MaxBytes * 3], canSeek: false);

        var result = await ReputationParser.ParseAsync(stream);

        Assert.Equal(ReputationParser.TooLargeError, result.Error);
        Assert.Equal(ReputationParser.MaxBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task A_stream_with_a_valid_file_is_parsed()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(File(Entry())));

        var result = await ReputationParser.ParseAsync(stream);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Snapshot!.Factions);
    }

    [Theory]
    [InlineData("-1", "5", "10", "20")]
    [InlineData("1", "-5", "10", "20")]
    [InlineData("1", "5", "-10", "20")]
    [InlineData("1", "5", "10", "-20")]
    [InlineData("1", "5.5", "10", "20")]
    [InlineData("1", "5", "10", "99999999999")]
    [InlineData("1", "\"5\"", "10", "20")]
    [InlineData("null", "5", "10", "20")]
    public void An_entry_with_a_negative_fractional_huge_or_text_number_is_skipped_and_counted(string id, string rank, string rep, string toRank)
    {
        var result = Parse(File(Entry(id: id, rank: rank, rep: rep, toRank: toRank), Entry(id: "2", name: Q("Evil"))));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("Evil", Assert.Single(result.Snapshot!.Factions).Name);
    }

    [Fact]
    public void A_negative_spill_rep_skips_the_entry_but_a_missing_one_means_zero()
    {
        var withNegative = """{"factionId":3,"name":"Bad","rank":1,"rep":1,"repToRank":1,"spillRep":-4}""";

        var result = Parse(File(withNegative, Entry(name: Q("Good"))));

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, Assert.Single(result.Snapshot!.Factions).SpillRep);
    }

    [Fact]
    public void A_missing_property_or_a_non_object_entry_is_skipped()
    {
        var noRank = """{"factionId":4,"name":"No Rank","rep":1,"repToRank":1}""";
        var noRep = """{"factionId":5,"name":"No Rep","rank":1,"repToRank":1}""";
        var noToRank = """{"factionId":6,"name":"No Goal","rank":1,"rep":1}""";
        var noId = """{"name":"No Id","rank":1,"rep":1,"repToRank":1}""";

        var result = Parse(File(noRank, noRep, noToRank, noId, "7", "null", Entry(name: Q("Good"))));

        Assert.Equal(6, result.SkippedCount);
        Assert.Single(result.Snapshot!.Factions);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"\\u0007\\n\"")]
    [InlineData("12")]
    [InlineData("null")]
    public void A_missing_blank_or_non_text_name_skips_the_entry(string name)
    {
        var result = Parse(File(Entry(name: name), Entry(id: "2", name: Q("Evil"))));

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("Evil", Assert.Single(result.Snapshot!.Factions).Name);
    }

    [Fact]
    public void A_file_whose_entries_are_all_invalid_is_an_error()
    {
        var result = Parse(File(Entry(name: "\"\""), Entry(id: "-2")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ReputationParser.NoValidEntriesError, result.Error);
    }

    [Fact]
    public void A_repeated_faction_id_keeps_the_first_and_counts_the_rest_as_skipped()
    {
        var result = Parse(File(Entry(id: "1", name: Q("First")), Entry(id: "1", name: Q("Second")), Entry(id: "2", name: Q("Other"))));

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(["First", "Other"], result.Snapshot!.Factions.Select(f => f.Name));
    }

    [Fact]
    public void Text_is_cleaned_and_clamped()
    {
        var longName = new string('n', 500);
        var json = $$"""{"schema":1,"exportedAt":"{{new string('t', 100)}}","factions":[{{Entry(name: Q("  Go\\u0001od\\t "))}},{{Entry(id: "2", name: Q(longName))}}]}""";

        var result = Parse(json);

        Assert.Equal("Good", result.Snapshot!.Factions[0].Name);
        Assert.Equal(200, result.Snapshot.Factions[1].Name.Length);
        Assert.Equal(40, result.Snapshot.ExportedAt!.Length);
    }

    [Fact]
    public void A_missing_or_non_text_export_time_is_simply_absent()
    {
        Assert.Null(Parse(File(Entry())).Snapshot!.ExportedAt);
        Assert.Null(Parse($$"""{"schema":1,"exportedAt":5,"factions":[{{Entry()}}]}""").Snapshot!.ExportedAt);
    }

    [Fact]
    public void Exactly_500_entries_are_accepted_and_501_are_an_error()
    {
        var ok = Parse(File(Enumerable.Range(1, 500).Select(i => Entry(id: i.ToString())).ToArray()));
        var tooMany = Parse(File(Enumerable.Range(1, 501).Select(i => Entry(id: i.ToString())).ToArray()));

        Assert.Equal(500, ok.Snapshot!.Factions.Count);
        Assert.Equal(ReputationParser.TooManyFactionsError, tooMany.Error);
        Assert.Null(tooMany.Snapshot);
    }

    [Fact]
    public void Error_messages_never_contain_file_content()
    {
        var result = Parse("""{"schema":"secret-value","factions":[]}""");

        Assert.DoesNotContain("secret-value", result.Error);
    }

    private sealed class CountingStream(byte[] data, bool canSeek = true) : Stream
    {
        private int _position;

        public int BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => canSeek;

        public override bool CanWrite => false;

        public override long Length => canSeek ? data.Length : throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, Math.Min(data.Length - _position, 64 * 1024));
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            BytesRead += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
