using System.Diagnostics;
using System.Text.Json;
using AqwCompanion.Core.Diagnostics;

namespace AqwCompanion.Tests.Diagnostics;

public class SnapshotRedactorTests
{
    private readonly SnapshotRedactor _redactor = new();

    [Fact]
    public void Redacts_email_addresses()
    {
        var output = _redactor.Redact("contact: someone@example.com now");

        Assert.DoesNotContain("someone@example.com", output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    [Fact]
    public void Redacts_whole_cookie_header_lines()
    {
        var output = _redactor.Redact("Cookie: sid=abc123; other=xyz789\nAccept: */*");

        Assert.DoesNotContain("abc123", output);
        Assert.DoesNotContain("xyz789", output);
        Assert.Contains("Accept: */*", output);
    }

    [Fact]
    public void Redacts_set_cookie_header_lines()
    {
        var output = _redactor.Redact("Set-Cookie: session=topsecret; Path=/");

        Assert.DoesNotContain("topsecret", output);
    }

    [Fact]
    public void Redacts_bearer_tokens()
    {
        var output = _redactor.Redact("Authorization: Bearer eyJhbGciOi.payload.sig");

        Assert.DoesNotContain("eyJhbGciOi", output);
        Assert.Contains("Bearer", output);
    }

    [Theory]
    [InlineData("""{"token":"abc123"}""", "abc123")]
    [InlineData("""{"sessionId":"s3cr3t"}""", "s3cr3t")]
    [InlineData("""{"csrf_token":"c5rf"}""", "c5rf")]
    [InlineData("https://x.test/a?access_token=tok999&page=2", "tok999")]
    [InlineData("password = 'hunter2'", "hunter2")]
    public void Redacts_values_of_sensitive_keys(string input, string secret)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain(secret, output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    [Fact]
    public void Keeps_unrelated_content_intact()
    {
        const string html = "<table id=\"inventoryTable\"><tr><td class=\"name\">Sword</td></tr></table>";

        Assert.Equal(html, _redactor.Redact(html));
    }

    [Fact]
    public void Redacts_provided_sensitive_terms_case_insensitively_and_ignores_blanks()
    {
        var redactor = new SnapshotRedactor(["MyAccountName", "  ", ""]);

        var output = redactor.Redact("Welcome myaccountname and MYACCOUNTNAME!");

        Assert.DoesNotContain("myaccountname", output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"Welcome {SnapshotRedactor.Mask} and {SnapshotRedactor.Mask}!", output);
    }

    [Fact]
    public void Redaction_is_idempotent()
    {
        var once = _redactor.Redact("""{"token":"abc123"} a@b.co""");

        Assert.Equal(once, _redactor.Redact(once));
    }

    // Finding 1: HTML attribute form (secret lives in a sibling value/content attribute).
    [Theory]
    [InlineData("""<input name="csrf" value="abc123">""")]
    [InlineData("""<meta name="csrf-token" content="abc123">""")]
    [InlineData("""<input type="hidden" value="abc123" name="csrf_token" />""")]
    [InlineData("""<input name='session_id' value=abc123>""")]
    public void Redacts_html_attribute_form_secrets(string input)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain("abc123", output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    [Fact]
    public void Keeps_html_inputs_with_non_sensitive_names_intact()
    {
        const string html = """<input name="quantity" value="5"><meta name="viewport" content="width=device-width">""";

        Assert.Equal(html, _redactor.Redact(html));
    }

    // Finding 2: JSON embedded in a string, and single-quoted keys.
    [Theory]
    [InlineData("""{\"token\":\"abc123\"}""")]
    [InlineData("""{'token': 'abc123'}""")]
    [InlineData("""{\"session\":{\"id\":\"abc123\"}}""")]
    public void Redacts_escaped_and_single_quoted_keys(string input)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain("abc123", output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    [Fact]
    public void Escaped_json_stays_well_formed_when_redacted()
    {
        var output = _redactor.Redact("""{\"token\":\"abc123\"}""");

        Assert.Equal("""{\"token\":\"[REDACTED]\"}""", output);
    }

    // Finding 3: cookies outside a line-start header.
    [Theory]
    [InlineData("""{"Cookie":"sid=abc123"}""")]
    [InlineData("""document.cookie = "sid=abc123; path=/";""")]
    [InlineData("Request headers: Cookie: sid=abc123; other=def456")]
    [InlineData("""{"Set-Cookie": "sid=abc123"}""")]
    public void Redacts_cookies_outside_line_start_headers(string input)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain("abc123", output);
        Assert.DoesNotContain("def456", output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    // Finding 4: object, array and escaped-quote values.
    [Theory]
    [InlineData("""{"session":{"id":"abc123"}}""", "abc123")]
    [InlineData("""{"tokens":["abc123","def456"]}""", "abc123")]
    [InlineData("""{"tokens":["abc123","def456"]}""", "def456")]
    [InlineData("""{"password":"ab\"cd"}""", "cd\"")]
    [InlineData("""{"token":123456}""", "123456")]
    [InlineData("""{"a":{"token":{"x":[1,{"y":"abc123"}]},"keep":"yes"}}""", "abc123")]
    public void Redacts_structured_and_escaped_values_without_breaking_json(string input, string secret)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain(secret, output);
        Assert.DoesNotContain("\"\"", output);
        Assert.Contains(SnapshotRedactor.Mask, output);
        using var _ = JsonDocument.Parse(output);
    }

    [Fact]
    public void Structured_redaction_keeps_sibling_content()
    {
        var output = _redactor.Redact("""{"session":{"id":"abc123"},"name":"Sword"}""");

        Assert.Equal("""{"session":"[REDACTED]","name":"Sword"}""", output);
    }

    // Finding 5: credential headers and key spellings.
    [Theory]
    [InlineData("X-Api-Key: abc123")]
    [InlineData("x-api-key=abc123")]
    [InlineData("Authorization: Basic abc123")]
    [InlineData("Authorization: Digest abc123")]
    [InlineData("Authorization: Token abc123")]
    [InlineData("Proxy-Authorization: Basic abc123")]
    [InlineData("Authorization: abc123")]
    [InlineData("""{"Authorization":"Basic abc123"}""")]
    public void Redacts_credential_headers_and_key_spellings(string input)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain("abc123", output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    [Fact]
    public void Keeps_authorization_scheme_visible()
    {
        Assert.Equal("Authorization: Basic [REDACTED]", _redactor.Redact("Authorization: Basic abc123"));
    }

    // Finding 6: URL-encoded email.
    [Theory]
    [InlineData("?email=user%40example.com&x=1", "user%40example.com")]
    [InlineData("?email=user%2540example.com", "user%2540example.com")]
    public void Redacts_url_encoded_emails(string input, string secret)
    {
        var output = _redactor.Redact(input);

        Assert.DoesNotContain(secret, output);
        Assert.Contains(SnapshotRedactor.Mask, output);
    }

    [Fact]
    public void Redaction_is_idempotent_for_rich_samples()
    {
        const string sample = """
            Cookie: sid=abc123; other=def456
            Authorization: Bearer eyJhbGciOi.payload.sig
            Authorization: Basic abc123
            X-Api-Key: abc123
            <input name="csrf" value="abc123"><meta name="csrf-token" content=abc123>
            {"session":{"id":"abc123"},"tokens":["a","b"],"password":"ab\"cd","n":{"token":5}}
            {\"token\":\"abc123\"} {'secret': 'abc123'} document.cookie = "sid=abc123";
            ?email=user%40example.com&access_token=tok999&page=2 a@b.co
            """;
        var redactor = _redactor;

        var once = redactor.Redact(sample);

        Assert.Equal(once, redactor.Redact(once));
        Assert.DoesNotContain("abc123", once);
        Assert.DoesNotContain("def456", once);
        Assert.DoesNotContain("tok999", once);
        Assert.DoesNotContain("eyJhbGciOi", once);
    }

    [Theory]
    [InlineData("a", 600_000)]
    [InlineData("token", 120_000)]
    [InlineData("a@", 300_000)]
    [InlineData("a.", 300_000)]
    [InlineData("\"token\":{", 60_000)]
    [InlineData("password='", 60_000)]
    [InlineData("<input name=\"csrf\" ", 60_000)]
    [InlineData("Bearer ", 100_000)]
    [InlineData("\"token\":\"x\",", 60_000)]
    [InlineData("Cookie: a=b\n", 60_000)]
    public void Runs_in_linear_time_on_large_hostile_inputs(string unit, int repeat)
    {
        var input = string.Concat(Enumerable.Repeat(unit, repeat));
        var redactor = new SnapshotRedactor(["secretterm"]);

        var stopwatch = Stopwatch.StartNew();
        var output = redactor.Redact(input);
        stopwatch.Stop();

        Assert.NotNull(output);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }
}
