using System.Diagnostics;
using System.IO.Compression;
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

    private static DecodeFailure FailureOf(byte[] message)
    {
        using var decoded = MessageDecoder.TryDecode(message, out var failure);
        Assert.Null(decoded);
        return failure;
    }

    [Fact]
    public void A_decoded_message_has_no_failure()
    {
        using var plain = MessageDecoder.TryDecode(PlainJson(Envelope("ct")), out var plainFailure);
        using var blob = MessageDecoder.TryDecode(EncodeBlob(Envelope("ct", Body())), out var blobFailure);

        Assert.NotNull(plain);
        Assert.NotNull(blob);
        Assert.Equal(DecodeFailure.None, plainFailure);
        Assert.Equal(DecodeFailure.None, blobFailure);
    }

    [Theory]
    [InlineData("%xt%zm%something%1%2%3%4%5%6%7%8%9%10%11%12%13%")]
    [InlineData("<msg t='sys'><body action='joinOK' r='1'><vars/></body></msg>")]
    [InlineData("ZAAAA")]
    [InlineData("")]
    public void Text_xml_and_short_messages_are_not_blobs(string text)
    {
        Assert.Equal(DecodeFailure.NotBlob, FailureOf(Bytes(text)));
    }

    [Fact]
    public void A_message_over_the_size_cap_is_too_large()
    {
        Assert.Equal(DecodeFailure.TooLarge, FailureOf(Bytes(new string('A', MessageDecoder.MaxEncodedBytes + 1))));
        Assert.Equal(DecodeFailure.TooLarge, FailureOf(Bytes("{" + new string(' ', MessageDecoder.MaxEncodedBytes))));
    }

    [Fact]
    public void Text_that_is_no_base64_under_any_reading_is_not_base64()
    {
        Assert.Equal(DecodeFailure.NotBase64, FailureOf(Bytes("Z" + new string('!', 70))));
    }

    [Fact]
    public void Valid_base64_without_a_compression_header_is_reported_as_such()
    {
        Assert.Equal(DecodeFailure.NoCompressionHeader, FailureOf(Bytes("Z" + Convert.ToBase64String(Enumerable.Repeat((byte)0x41, 60).ToArray()))));
    }

    [Fact]
    public void A_compression_header_followed_by_garbage_is_an_inflate_failure()
    {
        var blob = new byte[] { 0x78, 0x9C }.Concat(Enumerable.Repeat((byte)0xFF, 60)).ToArray();

        Assert.Equal(DecodeFailure.InflateFailed, FailureOf(Bytes("Z" + Convert.ToBase64String(blob))));
    }

    [Fact]
    public void A_stream_that_inflates_past_the_cap_is_too_big()
    {
        var blob = EncodeBlobText(new byte[MessageDecoder.MaxInflatedBytes + 1]);

        Assert.True(blob.Length < MessageDecoder.MaxEncodedBytes);
        Assert.Equal(DecodeFailure.TooBigWhenInflated, FailureOf(blob));
    }

    [Fact]
    public void Content_that_does_not_start_with_a_brace_is_not_json()
    {
        Assert.Equal(DecodeFailure.InflatedNotJson, FailureOf(EncodeBlobText(Bytes("this is not json at all, it is just a long sentence"))));
    }

    [Fact]
    public void Broken_or_too_deep_json_is_invalid_json()
    {
        Assert.Equal(DecodeFailure.InvalidJson, FailureOf(Bytes("{\"b\":{\"o\":{\"cmd\":\"ct\"")));
        Assert.Equal(DecodeFailure.InvalidJson, FailureOf(EncodeBlobText(Bytes("{\"broken\": [1, 2, and a long tail of text here"))));
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", MessageDecoder.MaxJsonDepth + 1)) + "1" + new string('}', MessageDecoder.MaxJsonDepth + 1);
        Assert.Equal(DecodeFailure.InvalidJson, FailureOf(Bytes(deep)));
    }

    [Fact]
    public void Valid_json_without_a_command_has_no_command()
    {
        Assert.Equal(DecodeFailure.NoCommand, FailureOf(Bytes("{\"a\":1}")));
        Assert.Equal(DecodeFailure.NoCommand, FailureOf(EncodeBlob(new Dictionary<string, object?> { ["b"] = new Dictionary<string, object?> { ["o"] = new Dictionary<string, object?> { ["note"] = new string('a', 60) } } })));
    }

    private static byte[] Json(string cmd = "initUserData") => PlainJson(Envelope(cmd, Body()));

    private static byte[] RawDeflate(byte[] content)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(content);
        }
        return output.ToArray();
    }

    private static byte[] Header(int length) => Enumerable.Range(0, length).Select(i => (byte)(0xA0 + i % 16)).ToArray();

    private static byte[] Base64Of(byte[] blob, string marker = "Z") => Encoding.ASCII.GetBytes(marker + Convert.ToBase64String(blob));

    [Fact]
    public void Json_deflated_raw_behind_a_four_byte_header_is_decoded()
    {
        using var message = MessageDecoder.Decode(Base64Of([.. Header(4), .. RawDeflate(Json())]));

        Assert.NotNull(message);
        Assert.Equal("initUserData", message.Command);
    }

    [Fact]
    public void Zlib_behind_an_eleven_byte_header_is_decoded()
    {
        using var message = MessageDecoder.Decode(Base64Of([.. Header(11), .. Compress(Json("loadInventoryBig"))]));

        Assert.NotNull(message);
        Assert.Equal("loadInventoryBig", message.Command);
    }

    [Fact]
    public void A_stream_at_the_last_header_offset_is_found_and_one_byte_further_is_not()
    {
        using var found = MessageDecoder.Decode(Base64Of([.. Header(32), .. RawDeflate(Json())]));
        Assert.NotNull(found);

        Assert.Equal(DecodeFailure.NoCompressionHeader, FailureOf(Base64Of([.. Header(33), .. RawDeflate(Json())])));
        Assert.Equal(DecodeFailure.NoCompressionHeader, FailureOf(Base64Of([.. Header(33), .. Compress(Json())])));
    }

    [Fact]
    public void Inflated_data_that_is_mostly_binary_is_rejected()
    {
        var junk = new byte[400];
        new Random(7).NextBytes(junk);

        Assert.Equal(DecodeFailure.NoCompressionHeader, FailureOf(Base64Of([.. Header(6), .. RawDeflate(junk)])));
    }

    [Fact]
    public void Base64_inside_base64_is_decoded_one_level()
    {
        var inner = EncodeBlob(Envelope("loadInventoryBig", Body()), 0);

        using var message = MessageDecoder.Decode(Base64Of(inner));

        Assert.NotNull(message);
        Assert.Equal("loadInventoryBig", message.Command);
    }

    [Fact]
    public void Two_nested_levels_are_not_decoded()
    {
        var inner = EncodeBlob(Envelope("loadInventoryBig", Body()), 0);
        var twice = Base64Of(Encoding.ASCII.GetBytes(Convert.ToBase64String(inner)));

        Assert.Null(MessageDecoder.Decode(twice));
        Assert.Equal(DecodeFailure.NoCompressionHeader, FailureOf(twice));
    }

    [Fact]
    public void Decoding_a_two_megabyte_printable_blob_is_fast()
    {
        var printable = new byte[1_500_000];
        var random = new Random(11);
        for (var i = 0; i < printable.Length; i++)
        {
            printable[i] = (byte)random.Next(32, 127);
        }

        var encoded = Base64Of(printable, "");
        Assert.True(encoded.Length <= MessageDecoder.MaxEncodedBytes);
        var watch = Stopwatch.StartNew();
        var message = MessageDecoder.Decode(encoded);
        watch.Stop();

        Assert.Null(message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Decoding_a_two_megabyte_binary_blob_is_fast()
    {
        var binary = new byte[1_500_000];
        new Random(12).NextBytes(binary);

        var encoded = Base64Of(binary, "");
        Assert.True(encoded.Length <= MessageDecoder.MaxEncodedBytes);
        var watch = Stopwatch.StartNew();
        var message = MessageDecoder.Decode(encoded);
        watch.Stop();

        Assert.Null(message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Decoding_a_two_megabyte_base64_alphabet_blob_is_fast()
    {
        // Printable and itself valid base64, so the nested level also runs all its readings.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var random = new Random(13);
        var text = new byte[1_500_000];
        for (var i = 0; i < text.Length; i++)
        {
            text[i] = (byte)alphabet[random.Next(alphabet.Length)];
        }

        var encoded = Base64Of(text, "");
        var watch = Stopwatch.StartNew();
        var message = MessageDecoder.Decode(encoded);
        watch.Stop();

        Assert.Null(message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }
}
