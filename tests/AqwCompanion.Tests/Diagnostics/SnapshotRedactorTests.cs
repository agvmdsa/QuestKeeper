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
}
