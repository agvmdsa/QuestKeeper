using AqwCompanion.App.Infrastructure.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace AqwCompanion.Tests.Shell;

public sealed class PrivacyLogSinkTests
{
    private sealed class Collector : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class CustomType
    {
        public override string ToString() => "SECRET-TOSTRING";
    }

    private static LogEvent Event(string template, Exception? ex = null, params (string Name, LogEventPropertyValue Value)[] props) =>
        new(
            DateTimeOffset.Parse("2026-11-07T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            LogEventLevel.Warning,
            ex,
            new MessageTemplateParser().Parse(template),
            props.Select(p => new LogEventProperty(p.Name, p.Value)));

    private static LogEvent Run(LogEvent input)
    {
        var inner = new Collector();
        new PrivacyLogSink(inner).Emit(input);
        return Assert.Single(inner.Events);
    }

    private static string Scalar(LogEvent e, string name) => (string)((ScalarValue)e.Properties[name]).Value!;

    [Fact]
    public void Masks_strings_and_passes_safe_names_and_plain_data()
    {
        var output = Run(Event(
            "t",
            null,
            ("Value", new ScalarValue("SECRET")),
            ("ExceptionType", new ScalarValue("IOException")),
            ("SafeOperation", new ScalarValue("Load")),
            ("AppVersion", new ScalarValue("1.0")),
            ("Count", new ScalarValue(5)),
            ("Flag", new ScalarValue(true)),
            ("Kind", new ScalarValue(DayOfWeek.Monday)),
            ("Span", new ScalarValue(TimeSpan.FromSeconds(1))),
            ("Id", new ScalarValue(Guid.Empty)),
            ("Char", new ScalarValue('x'))));

        Assert.Equal("***", Scalar(output, "Value"));
        Assert.Equal("IOException", Scalar(output, "ExceptionType"));
        Assert.Equal("Load", Scalar(output, "SafeOperation"));
        Assert.Equal("1.0", Scalar(output, "AppVersion"));
        Assert.Equal(5, ((ScalarValue)output.Properties["Count"]).Value);
        Assert.Equal(true, ((ScalarValue)output.Properties["Flag"]).Value);
        Assert.Equal(DayOfWeek.Monday, ((ScalarValue)output.Properties["Kind"]).Value);
        Assert.Equal(TimeSpan.FromSeconds(1), ((ScalarValue)output.Properties["Span"]).Value);
        Assert.Equal(Guid.Empty, ((ScalarValue)output.Properties["Id"]).Value);
        Assert.Equal("***", Scalar(output, "Char"));
    }

    [Fact]
    public void Masks_nested_structures_sequences_and_dictionaries()
    {
        var structure = new StructureValue(
            [
                new LogEventProperty("Name", new ScalarValue("SECRET-A")),
                new LogEventProperty("N", new ScalarValue(3)),
                new LogEventProperty("List", new SequenceValue([new ScalarValue("SECRET-B"), new ScalarValue(7)])),
            ],
            "Thing");
        var dictionary = new DictionaryValue(
            new Dictionary<ScalarValue, LogEventPropertyValue> { [new ScalarValue("SECRET-KEY")] = new ScalarValue("SECRET-V") });

        var output = Run(Event("t", null, ("S", structure), ("D", dictionary)));

        var rendered = output.Properties["S"].ToString() + output.Properties["D"];
        Assert.DoesNotContain("SECRET", rendered);
        var rebuilt = (StructureValue)output.Properties["S"];
        Assert.Equal("Thing", rebuilt.TypeTag);
        Assert.Contains("3", rendered);
        Assert.Contains("7", rendered);
    }

    [Fact]
    public void Replaces_the_exception_by_a_summary_without_messages()
    {
        var ex = new InvalidOperationException("SECRET-MESSAGE", new ArgumentException("SECRET-INNER"));

        var output = Run(Event("t", ex));

        Assert.Null(output.Exception);
        var summary = Scalar(output, "ExceptionSummary");
        Assert.Contains("System.InvalidOperationException", summary);
        Assert.Contains("--- inner: System.ArgumentException", summary);
        Assert.DoesNotContain("SECRET", summary);
    }

    [Fact]
    public void Leaves_template_level_and_timestamp_untouched()
    {
        var input = Event("literal text {Value}", null, ("Value", new ScalarValue("x")));

        var output = Run(input);

        Assert.Equal(input.MessageTemplate.Text, output.MessageTemplate.Text);
        Assert.Equal(input.Level, output.Level);
        Assert.Equal(input.Timestamp, output.Timestamp);
    }

    [Fact]
    public void Masks_a_scalar_of_a_custom_type_that_renders_as_text()
    {
        var output = Run(Event("t", null, ("Thing", new ScalarValue(new CustomType()))));

        Assert.Equal("***", Scalar(output, "Thing"));
    }

    [Fact]
    public void Emits_a_fixed_event_when_sanitising_fails()
    {
        var hostile = new HostileValue();

        var output = Run(Event("SECRET-TEMPLATE {Value}", null, ("Value", hostile)));

        Assert.Equal("A log event could not be sanitised", output.MessageTemplate.Text);
        Assert.Empty(output.Properties);
        Assert.Equal(LogEventLevel.Warning, output.Level);
    }

    [Fact]
    public void Dispose_disposes_the_inner_sink()
    {
        var inner = new DisposableSink();

        new PrivacyLogSink(inner).Dispose();

        Assert.True(inner.Disposed);
    }

    private sealed class HostileValue : LogEventPropertyValue
    {
        public override void Render(TextWriter output, string? format = null, IFormatProvider? formatProvider = null) =>
            output.Write("x");
    }

    private sealed class DisposableSink : ILogEventSink, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Emit(LogEvent logEvent)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
