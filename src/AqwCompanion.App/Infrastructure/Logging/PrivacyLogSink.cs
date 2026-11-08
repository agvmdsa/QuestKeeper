using AqwCompanion.Core.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace AqwCompanion.App.Infrastructure.Logging;

/// <summary>
/// Wraps the real sink. Rewrites each log event so it can be written without leaking: the exception becomes a
/// type-and-stack summary, and every dynamic string is masked unless its property is explicitly marked safe.
/// </summary>
public sealed class PrivacyLogSink : ILogEventSink, IDisposable
{
    public const string Mask = "***";
    public const string SafePrefix = "Safe";
    public const string ExceptionSummaryProperty = "ExceptionSummary";

    private const string FailureTemplate = "A log event could not be sanitised";

    private static readonly HashSet<string> PassThroughNames =
        new(StringComparer.Ordinal) { "ExceptionType", ExceptionSummaryProperty, "AppVersion" };

    private readonly ILogEventSink _inner;

    public PrivacyLogSink(ILogEventSink inner)
    {
        _inner = inner;
    }

    public void Emit(LogEvent logEvent)
    {
        LogEvent sanitised;
        try
        {
            sanitised = Sanitise(logEvent);
        }
#pragma warning disable CA1031 // The guard must fail closed and never throw into the caller.
        catch (Exception)
        {
            sanitised = new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                null,
                new MessageTemplateParser().Parse(FailureTemplate),
                []);
        }
#pragma warning restore CA1031

        _inner.Emit(sanitised);
    }

    public void Dispose()
    {
        (_inner as IDisposable)?.Dispose();
    }

    private static LogEvent Sanitise(LogEvent logEvent)
    {
        var properties = new List<LogEventProperty>(logEvent.Properties.Count + 1);
        foreach (var property in logEvent.Properties)
        {
            properties.Add(new LogEventProperty(property.Key, SanitiseValue(property.Key, property.Value)));
        }

        if (logEvent.Exception is not null)
        {
            properties.RemoveAll(p => p.Name == ExceptionSummaryProperty);
            properties.Add(new LogEventProperty(
                ExceptionSummaryProperty,
                new ScalarValue(ExceptionSummary.Describe(logEvent.Exception))));
        }

        return new LogEvent(logEvent.Timestamp, logEvent.Level, null, logEvent.MessageTemplate, properties);
    }

    private static bool IsSafeName(string name) =>
        PassThroughNames.Contains(name) || name.StartsWith(SafePrefix, StringComparison.Ordinal);

    private static LogEventPropertyValue SanitiseValue(string name, LogEventPropertyValue value) =>
        value switch
        {
            ScalarValue scalar => SanitiseScalar(IsSafeName(name), scalar),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(e => SanitiseValue(string.Empty, e)).ToList()),
            StructureValue structure => new StructureValue(
                structure.Properties.Select(p => new LogEventProperty(p.Name, SanitiseValue(p.Name, p.Value))).ToList(),
                structure.TypeTag),
            DictionaryValue dictionary => SanitiseDictionary(dictionary),
            _ => throw new NotSupportedException("Unknown log value kind."),
        };

    private static ScalarValue SanitiseScalar(bool safe, ScalarValue scalar)
    {
        var raw = scalar.Value;
        if (raw is string or char)
        {
            return safe ? scalar : new ScalarValue(Mask);
        }

        return IsPlainData(raw) ? scalar : new ScalarValue(Mask);
    }

    private static bool IsPlainData(object? raw) =>
        raw is null or bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or Enum or DateTime or DateTimeOffset or TimeSpan or Guid
            or DateOnly or TimeOnly;

    private static DictionaryValue SanitiseDictionary(DictionaryValue dictionary)
    {
        var elements = new Dictionary<ScalarValue, LogEventPropertyValue>();
        foreach (var pair in dictionary.Elements)
        {
            var key = SanitiseScalar(safe: false, pair.Key);
            elements.TryAdd(key, SanitiseValue(string.Empty, pair.Value));
        }

        return new DictionaryValue(elements);
    }
}
