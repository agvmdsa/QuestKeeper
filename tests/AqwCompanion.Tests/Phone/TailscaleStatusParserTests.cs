using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.Phone;

public sealed class TailscaleStatusParserTests
{
    // Written from the documented shape of `tailscale status --json`; not yet checked against a real capture.
    private const string RunningWithCerts = """
        {"Version":"1.80.0","BackendState":"Running","Self":{"DNSName":"My-PC.tail1234.ts.net.","Online":true},
         "MagicDNSSuffix":"tail1234.ts.net","CertDomains":["my-pc.tail1234.ts.net"],"CurrentTailnet":{"Name":"a@b.c","MagicDNSEnabled":true}}
        """;

    private const string RunningWithoutCerts = """
        {"Version":"1.80.0","BackendState":"Running","Self":{"DNSName":"my-pc.tail1234.ts.net.","Online":true},
         "MagicDNSSuffix":"tail1234.ts.net","CertDomains":null,"CurrentTailnet":{"MagicDNSEnabled":true}}
        """;

    private const string NeedsLogin = """
        {"Version":"1.80.0","BackendState":"NeedsLogin","Self":null,"CertDomains":null,"AuthURL":"https://login.tailscale.com/a/xyz"}
        """;

    [Fact]
    public void Running_with_certificates_is_ready_with_a_clean_lowercase_name()
    {
        var state = TailscaleStatusParser.Parse(RunningWithCerts);

        Assert.Equal(TailscaleStateKind.Ready, state.Kind);
        Assert.Equal("my-pc.tail1234.ts.net", state.DnsName);
        Assert.Equal(TailscaleService.ReadyText, state.Text);
    }

    [Fact]
    public void Running_without_certificates_needs_https()
    {
        var state = TailscaleStatusParser.Parse(RunningWithoutCerts);

        Assert.Equal(TailscaleStateKind.HttpsNotEnabled, state.Kind);
        Assert.Null(state.DnsName);
        Assert.Equal(TailscaleService.HttpsNotEnabledText, state.Text);
    }

    [Fact]
    public void Magic_dns_off_needs_https()
    {
        var json = RunningWithCerts.Replace("\"MagicDNSEnabled\":true", "\"MagicDNSEnabled\":false");

        Assert.Equal(TailscaleStateKind.HttpsNotEnabled, TailscaleStatusParser.Parse(json).Kind);
    }

    [Theory]
    [InlineData("NeedsLogin")]
    [InlineData("NeedsMachineAuth")]
    [InlineData("Stopped")]
    public void Not_signed_in_states(string backend)
    {
        var state = TailscaleStatusParser.Parse(NeedsLogin.Replace("NeedsLogin", backend));

        Assert.Equal(TailscaleStateKind.NotSignedIn, state.Kind);
        Assert.Equal(TailscaleService.NotSignedInText, state.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"BackendState\":\"Starting\"}")]
    [InlineData("{\"BackendState\":5}")]
    [InlineData("{\"BackendState\":\"Running\",\"Self\":{},\"CertDomains\":[\"x.ts.net\"]}")]
    [InlineData("{\"BackendState\":\"Running\",\"Self\":{\"DNSName\":\"evil.com/x.\"},\"CertDomains\":[\"x.ts.net\"]}")]
    [InlineData("{\"BackendState\":\"Running\",\"Self\":{\"DNSName\":\"a.ts.net\"},\"CertDomains\":\"x\"}")]
    public void Garbage_is_an_error(string json)
    {
        var state = TailscaleStatusParser.Parse(json);

        Assert.Equal(TailscaleStateKind.Error, state.Kind);
        Assert.Null(state.DnsName);
        Assert.Equal(TailscaleService.ErrorText, state.Text);
    }

    [Fact]
    public void Oversized_json_is_an_error()
    {
        var json = RunningWithCerts.Replace("\"Version\":\"1.80.0\"", "\"Version\":\"" + new string('a', 300_000) + "\"");

        Assert.Equal(TailscaleStateKind.Error, TailscaleStatusParser.Parse(json).Kind);
    }

    [Fact]
    public void Deeply_nested_json_is_an_error_not_a_throw()
    {
        var json = new string('[', 5000) + new string(']', 5000);

        Assert.Equal(TailscaleStateKind.Error, TailscaleStatusParser.Parse(json).Kind);
    }

    [Theory]
    [InlineData("my-pc.tail1234.ts.net", true)]
    [InlineData("MY-PC.tail1234.ts.net", true)]
    [InlineData("my-pc.tail1234.ts.net.", true)]
    [InlineData("my_pc.ts.net", false)]
    [InlineData("evil.com/x", false)]
    [InlineData("a b.ts.net", false)]
    [InlineData("", false)]
    [InlineData("a..b.ts.net", false)]
    [InlineData(".a.ts.net", false)]
    [InlineData("a.ts.net..", false)]
    [InlineData("localhost", false)]
    public void Dns_name_validity(string name, bool valid) =>
        Assert.Equal(valid, TailscaleStatusParser.IsValidDnsName(name));

    [Fact]
    public void Dns_name_length_limit()
    {
        var ok = new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        Assert.Equal(253, ok.Length);
        Assert.True(TailscaleStatusParser.IsValidDnsName(ok));
        Assert.False(TailscaleStatusParser.IsValidDnsName(ok + "d"));
        Assert.False(TailscaleStatusParser.IsValidDnsName(new string('a', 127) + "." + new string('b', 126)));
    }
}
