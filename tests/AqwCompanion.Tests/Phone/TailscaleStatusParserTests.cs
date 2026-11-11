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

    [Fact]
    public void Stopped_says_turned_off_not_signed_out()
    {
        var state = TailscaleStatusParser.Parse(NeedsLogin.Replace("NeedsLogin", "Stopped"));

        Assert.Equal(TailscaleStateKind.NotSignedIn, state.Kind);
        Assert.Equal(TailscaleService.StoppedText, state.Text);
    }

    [Fact]
    public void Self_name_missing_from_cert_domains_needs_https()
    {
        var json = RunningWithCerts.Replace("\"CertDomains\":[\"my-pc.tail1234.ts.net\"]", "\"CertDomains\":[\"other.tail1234.ts.net\"]");

        Assert.Equal(TailscaleStateKind.HttpsNotEnabled, TailscaleStatusParser.Parse(json).Kind);
    }

    [Fact]
    public void Cert_domain_match_ignores_case_and_trailing_dot()
    {
        var json = RunningWithCerts.Replace("\"CertDomains\":[\"my-pc.tail1234.ts.net\"]", "\"CertDomains\":[\"MY-PC.tail1234.ts.net.\"]");

        Assert.Equal(TailscaleStateKind.Ready, TailscaleStatusParser.Parse(json).Kind);
    }

    [Fact]
    public void Running_with_certs_but_null_self_is_an_error()
    {
        var json = """{"BackendState":"Running","Self":null,"CertDomains":["my-pc.tail1234.ts.net"]}""";

        Assert.Equal(TailscaleStateKind.Error, TailscaleStatusParser.Parse(json).Kind);
    }

    [Theory]
    [InlineData("NeedsLogin")]
    [InlineData("NeedsMachineAuth")]
    public void Not_signed_in_states(string backend)
    {
        var state = TailscaleStatusParser.Parse(NeedsLogin.Replace("NeedsLogin", backend));

        Assert.Equal(TailscaleStateKind.NotSignedIn, state.Kind);
        Assert.Equal(TailscaleService.NotSignedInText, state.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    [InlineData("{\"BackendState\":\"Starting\"}")]
    [InlineData("{\"BackendState\":\"NoState\"}")]
    public void No_output_or_a_backend_that_is_not_up_yet_says_tailscale_is_not_running(string json)
    {
        // tailscale.exe prints nothing on stdout when tailscaled is not running (the complaint goes to stderr, which is not read).
        var state = TailscaleStatusParser.Parse(json);

        Assert.Equal(TailscaleStateKind.Error, state.Kind);
        Assert.Null(state.DnsName);
        Assert.Equal(TailscaleService.NotRunningText, state.Text);
        Assert.Equal("Tailscale does not seem to be running. Start Tailscale from the Start menu, then try again.", TailscaleService.NotRunningText);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"BackendState\":\"Unknown\"}")]
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
    [InlineData("-a.ts.net", false)]
    [InlineData("a-.ts.net", false)]
    [InlineData("a.-b.ts.net", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("a.b.123", false)]
    public void Dns_name_validity(string name, bool valid) =>
        Assert.Equal(valid, TailscaleStatusParser.IsValidDnsName(name));

    [Fact]
    public void A_label_of_64_characters_is_rejected_and_63_accepted()
    {
        Assert.True(TailscaleStatusParser.IsValidDnsName(new string('a', 63) + ".ts.net"));
        Assert.False(TailscaleStatusParser.IsValidDnsName(new string('a', 64) + ".ts.net"));
    }

    [Fact]
    public void Dns_name_length_limit()
    {
        var ok = new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        Assert.Equal(253, ok.Length);
        Assert.True(TailscaleStatusParser.IsValidDnsName(ok));
        Assert.False(TailscaleStatusParser.IsValidDnsName(ok + "d"));
        Assert.False(TailscaleStatusParser.IsValidDnsName(new string('a', 127) + "." + new string('b', 126)));
    }

    // Redacted copy of the shape a real Tailscale 1.104.6 on Windows printed.
    private static string RealStatus() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Phone", "tailscale-status-1.104.6.json"));

    [Fact]
    public void A_real_Tailscale_1_104_6_status_is_ready_and_ignores_the_peers_and_unknown_fields()
    {
        var state = TailscaleStatusParser.Parse(RealStatus());

        Assert.Equal(TailscaleStateKind.Ready, state.Kind);
        Assert.Equal("my-pc.tail1234.ts.net", state.DnsName);
        Assert.Equal("Tailscale is ready.", state.Text);
    }

    [Fact]
    public void A_real_status_without_certificate_domains_needs_https()
    {
        var json = RealStatus().Replace("\"CertDomains\": [\"my-pc.tail1234.ts.net\"]", "\"CertDomains\": null");
        Assert.NotEqual(RealStatus(), json);

        Assert.Equal(TailscaleStateKind.HttpsNotEnabled, TailscaleStatusParser.Parse(json).Kind);
    }

    [Fact]
    public void A_real_status_with_MagicDNS_off_needs_https()
    {
        var json = RealStatus().Replace("\"MagicDNSEnabled\": true", "\"MagicDNSEnabled\": false");
        Assert.NotEqual(RealStatus(), json);

        Assert.Equal(TailscaleStateKind.HttpsNotEnabled, TailscaleStatusParser.Parse(json).Kind);
    }

    [Fact]
    public void A_real_status_that_needs_login_is_not_signed_in()
    {
        var json = RealStatus().Replace("\"BackendState\": \"Running\"", "\"BackendState\": \"NeedsLogin\"");
        Assert.NotEqual(RealStatus(), json);

        var state = TailscaleStatusParser.Parse(json);

        Assert.Equal(TailscaleStateKind.NotSignedIn, state.Kind);
        Assert.Equal(TailscaleService.NotSignedInText, state.Text);
    }

    [Fact]
    public void A_real_status_that_is_stopped_says_tailscale_is_turned_off()
    {
        var json = RealStatus().Replace("\"BackendState\": \"Running\"", "\"BackendState\": \"Stopped\"");
        Assert.NotEqual(RealStatus(), json);

        var state = TailscaleStatusParser.Parse(json);

        Assert.Equal(TailscaleStateKind.NotSignedIn, state.Kind);
        Assert.Equal(TailscaleService.StoppedText, state.Text);
    }

    [Fact]
    public void A_real_status_without_Self_is_an_error()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(RealStatus())!.AsObject();
        Assert.True(node.Remove("Self"));

        Assert.Equal(TailscaleStateKind.Error, TailscaleStatusParser.Parse(node.ToJsonString()).Kind);
    }
}
