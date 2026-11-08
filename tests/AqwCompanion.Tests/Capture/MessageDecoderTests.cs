using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AqwCompanion.Core.Capture;
using static AqwCompanion.Tests.Capture.GameCaptureBuilder;

namespace AqwCompanion.Tests.Capture;

public class MessageDecoderTests
{
    private static Dictionary<string, object?> Body() => new() { ["note"] = new string('a', 80) };

    [Fact]
    public void Plain_json_is_parsed_and_the_command_read()
    {
        using var message = MessageDecoder.Decode(PlainJson(Envelope("ct")));

        Assert.NotNull(message);
        Assert.Equal("ct", message.Command);
        Assert.Equal(JsonValueKind.Object, message.Document.RootElement.ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void An_encoded_blob_is_decoded_with_any_number_of_marker_characters(int markers)
    {
        using var message = MessageDecoder.Decode(EncodeBlob(Envelope("loadInventoryBig", Body()), markers));

        Assert.NotNull(message);
        Assert.Equal("loadInventoryBig", message.Command);
        Assert.Equal(new string('a', 80), message.Document.RootElement.GetProperty("b").GetProperty("o").GetProperty("note").GetString());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    public void Missing_base64_padding_is_tolerated(int markers, bool strip)
    {
        // Vary the body length so that every padding amount (0, 1, 2) is met.
        for (var extra = 0; extra < 4; extra++)
        {
            var body = new Dictionary<string, object?> { ["note"] = new string('a', 80 + extra) };

            using var message = MessageDecoder.Decode(EncodeBlob(Envelope("initUserData", body), markers, stripPadding: strip));

            Assert.NotNull(message);
            Assert.Equal("initUserData", message.Command);
        }
    }

    [Fact]
    public void A_gzip_stream_is_decoded_like_a_zlib_stream()
    {
        using var message = MessageDecoder.Decode(EncodeBlob(Envelope("loadInventoryBig", Body()), 1, gzip: true));

        Assert.NotNull(message);
        Assert.Equal("loadInventoryBig", message.Command);
    }

    [Fact]
    public void Text_messages_give_nothing()
    {
        Assert.Null(MessageDecoder.Decode(Bytes("%xt%loginResponse%-1%true%1234%some text that is long enough to pass the minimum%")));
    }

    [Fact]
    public void Xml_gives_nothing()
    {
        Assert.Null(MessageDecoder.Decode(Bytes("<msg t='sys'><body action='logOK' r='0'><vars></vars></body></msg>")));
    }

    [Fact]
    public void Random_base64_gives_nothing()
    {
        var random = new byte[200];
        new Random(42).NextBytes(random);

        Assert.Null(MessageDecoder.Decode(Encoding.ASCII.GetBytes("Z" + Convert.ToBase64String(random))));
    }

    [Fact]
    public void A_zlib_bomb_gives_nothing_promptly()
    {
        var bomb = new byte[MessageDecoder.MaxInflatedBytes + 1024 * 1024];
        Array.Fill(bomb, (byte)' ');
        bomb[0] = (byte)'{';
        var encoded = EncodeBlobText(bomb);
        Assert.True(encoded.Length < 100 * 1024, "the bomb must be small on the wire");

        var watch = Stopwatch.StartNew();
        var message = MessageDecoder.Decode(encoded);

        Assert.Null(message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Content_just_under_the_inflated_limit_is_accepted()
    {
        var body = new Dictionary<string, object?> { ["pad"] = new string('p', MessageDecoder.MaxInflatedBytes - 1024) };

        using var message = MessageDecoder.Decode(EncodeBlob(Envelope("big", body)));

        Assert.NotNull(message);
    }

    [Fact]
    public void A_message_over_two_megabytes_gives_nothing()
    {
        var big = new byte[MessageDecoder.MaxEncodedBytes + 1];
        Array.Fill(big, (byte)' ');
        big[0] = (byte)'{';

        Assert.Null(MessageDecoder.Decode(big));
    }

    [Fact]
    public void Json_nested_deeper_than_sixteen_levels_gives_nothing()
    {
        var deep = "{\"b\":{\"o\":{\"cmd\":\"x\",\"d\":" + new string('[', 20) + new string(']', 20) + "}}}";

        Assert.Null(MessageDecoder.Decode(Bytes(deep)));
    }

    [Fact]
    public void Json_nested_within_sixteen_levels_is_accepted()
    {
        var shallow = "{\"b\":{\"o\":{\"cmd\":\"x\",\"d\":" + new string('[', 10) + new string(']', 10) + "}}}";

        using var message = MessageDecoder.Decode(Bytes(shallow));

        Assert.NotNull(message);
    }

    [Theory]
    [InlineData("{\"b\":{\"o\":{\"other\":1}}}")]
    [InlineData("{\"b\":{\"o\":{\"cmd\":5}}}")]
    [InlineData("{\"b\":{\"o\":{\"cmd\":\"\"}}}")]
    [InlineData("{\"b\":[1,2]}")]
    [InlineData("{\"x\":1}")]
    [InlineData("{not json at all")]
    [InlineData("{\"b\":{\"o\":{\"cmd\":\"x\"}}} trailing")]
    public void Json_without_a_command_or_invalid_gives_nothing(string json)
    {
        Assert.Null(MessageDecoder.Decode(Bytes(json)));
    }

    [Fact]
    public void A_truncated_zlib_stream_gives_nothing()
    {
        var blob = Convert.FromBase64String(Encoding.ASCII.GetString(EncodeBlob(Envelope("x", Body()), 0)));
        var truncated = Convert.ToBase64String(blob.AsSpan(0, blob.Length / 2));

        Assert.Null(MessageDecoder.Decode(Encoding.ASCII.GetBytes(truncated)));
    }

    [Fact]
    public void An_empty_span_gives_nothing()
    {
        Assert.Null(MessageDecoder.Decode([]));
    }

    [Fact]
    public void Short_unrecognised_bytes_give_nothing()
    {
        Assert.Null(MessageDecoder.Decode([1, 2, 3]));
    }

    [Fact]
    public void The_document_is_disposed_with_the_message()
    {
        var message = MessageDecoder.Decode(PlainJson(Envelope("ct")));
        Assert.NotNull(message);
        var document = message.Document;

        message.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.RootElement.ValueKind);
    }
}
