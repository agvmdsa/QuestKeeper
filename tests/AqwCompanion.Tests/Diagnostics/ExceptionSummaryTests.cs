using AqwCompanion.Core.Diagnostics;

namespace AqwCompanion.Tests.Diagnostics;

public sealed class ExceptionSummaryTests
{
    private static InvalidOperationException Thrown(string message, Exception? inner = null)
    {
        try
        {
            throw new InvalidOperationException(message, inner);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Contains_type_full_name_and_the_throwing_frame()
    {
        var text = ExceptionSummary.Describe(Thrown("SECRET-MESSAGE"));

        Assert.StartsWith("System.InvalidOperationException", text);
        Assert.Contains("   at AqwCompanion.Tests.Diagnostics.ExceptionSummaryTests.Thrown", text);
    }

    [Fact]
    public void Never_contains_the_message_or_the_data_bag()
    {
        var ex = Thrown("SECRET-MESSAGE");
        ex.Data["k"] = "SECRET-DATA";

        var text = ExceptionSummary.Describe(ex);

        Assert.DoesNotContain("SECRET-MESSAGE", text);
        Assert.DoesNotContain("SECRET-DATA", text);
    }

    [Fact]
    public void Lists_inner_type_without_its_message()
    {
        var inner = new ArgumentException("SECRET-INNER");
        var text = ExceptionSummary.Describe(Thrown("outer", inner));

        Assert.Contains("--- inner: System.ArgumentException", text);
        Assert.DoesNotContain("SECRET-INNER", text);
    }

    [Fact]
    public void Aggregate_lists_each_inner_exception()
    {
        var aggregate = new AggregateException("SECRET-AGG", new TimeoutException("a"), new FormatException("b"));

        var text = ExceptionSummary.Describe(aggregate);

        Assert.Contains("--- inner: System.TimeoutException", text);
        Assert.Contains("--- inner: System.FormatException", text);
        Assert.DoesNotContain("SECRET-AGG", text);
    }

    [Fact]
    public void An_exception_that_was_never_thrown_yields_only_the_type_line()
    {
        Assert.Equal("System.InvalidOperationException", ExceptionSummary.Describe(new InvalidOperationException("x")));
    }

    [Fact]
    public void Inner_depth_is_limited_to_five()
    {
        Exception chain = new InvalidOperationException("0");
        for (var i = 1; i <= 8; i++)
        {
            chain = new InvalidOperationException(i.ToString(System.Globalization.CultureInfo.InvariantCulture), chain);
        }

        var text = ExceptionSummary.Describe(chain);

        Assert.Equal(5, text.Split("--- inner:").Length - 1);
    }

    [Fact]
    public void A_hostile_exception_does_not_make_describe_throw()
    {
        var text = ExceptionSummary.Describe(new HostileException());

        Assert.Contains("HostileException", text);
    }

    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("boom");

        public override string? StackTrace => throw new InvalidOperationException("boom");

        public override string ToString() => throw new InvalidOperationException("boom");
    }
}
