using System.Text;

namespace AqwCompanion.Core.Diagnostics;

/// <summary>
/// Describes an exception without any text the program may have put in it: the type and the call stack only.
/// Never includes the message, the data bag, the help link or any property value.
/// </summary>
public static class ExceptionSummary
{
    private const int MaxInner = 5;
    private const string UnknownType = "(unknown exception type)";

    /// <summary>
    /// Returns the type full name, then one line per stack frame exactly as <see cref="Exception.StackTrace"/> gives them,
    /// then for each inner exception (depth first, at most five) a line "--- inner: type" and its frames.
    /// Aggregate exceptions summarise each inner exception. Never throws.
    /// </summary>
    public static string Describe(Exception exception)
    {
        var builder = new StringBuilder();
        try
        {
            AppendHeaderAndFrames(builder, exception, inner: false);
            var remaining = MaxInner;
            AppendInner(builder, exception, ref remaining);
        }
#pragma warning disable CA1031 // The summary is used on failure paths: it must never throw.
        catch (Exception)
        {
            if (builder.Length == 0)
            {
                builder.Append(UnknownType);
            }
        }
#pragma warning restore CA1031

        return builder.ToString();
    }

    private static void AppendInner(StringBuilder builder, Exception exception, ref int remaining)
    {
        foreach (var inner in InnerOf(exception))
        {
            if (remaining <= 0)
            {
                return;
            }

            remaining--;
            builder.AppendLine();
            AppendHeaderAndFrames(builder, inner, inner: true);
            AppendInner(builder, inner, ref remaining);
        }
    }

    private static IEnumerable<Exception> InnerOf(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions;
        }

        return exception.InnerException is null ? [] : [exception.InnerException];
    }

    private static void AppendHeaderAndFrames(StringBuilder builder, Exception exception, bool inner)
    {
        if (inner)
        {
            builder.Append("--- inner: ");
        }

        builder.Append(TypeName(exception));

        var stack = StackOf(exception);
        if (!string.IsNullOrEmpty(stack))
        {
            builder.AppendLine().Append(stack);
        }
    }

    private static string TypeName(Exception exception)
    {
        try
        {
            var type = exception.GetType();
            return type.FullName ?? type.Name;
        }
#pragma warning disable CA1031
        catch (Exception)
        {
            return UnknownType;
        }
#pragma warning restore CA1031
    }

    private static string? StackOf(Exception exception)
    {
        try
        {
            return exception.StackTrace;
        }
#pragma warning disable CA1031
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031
    }
}
