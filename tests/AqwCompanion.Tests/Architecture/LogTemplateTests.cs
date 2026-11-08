using System.Text.RegularExpressions;

namespace AqwCompanion.Tests.Architecture;

/// <summary>
/// The log guard rewrites property values but not the message template, so a value must never be interpolated into a template.
/// Messages are fixed text with {Placeholders}; the guard masks what fills them.
/// </summary>
public partial class LogTemplateTests
{
    // Log.Error($"..."), Log.Error(ex, $"..."), logger.LogWarning($"..."), _log.Information(ex, $@"...") and the like.
    [GeneratedRegex(
        """\b(?:Log|_log|log|_logger|logger)\.(?:Verbose|Debug|Information|Warning|Error|Fatal|Write|LogTrace|LogDebug|LogInformation|LogWarning|LogError|LogCritical)\(\s*(?:[A-Za-z_][\w.]*\s*,\s*)?\$@?"{1,3}""")]
    private static partial Regex InterpolatedLogCall();

    [Fact]
    public void No_log_call_interpolates_a_value_into_the_template()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionRoot(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in InterpolatedLogCall().Matches(text))
            {
                var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                offenders.Add($"{Path.GetRelativePath(SolutionRoot(), file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0, "Interpolated log templates: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("Log.Error($\"failed {value}\");")]
    [InlineData("Log.Error(ex, $\"failed {value}\");")]
    [InlineData("_logger.LogWarning($\"failed {value}\");")]
    [InlineData("Log.Information(ex, $@\"failed {value}\");")]
    public void The_pattern_recognises_interpolated_templates(string code) =>
        Assert.Matches(InterpolatedLogCall(), code);

    [Theory]
    [InlineData("Log.Error(\"failed {SafeValue}\", value);")]
    [InlineData("Log.Error(ex, \"failed {SafeValue}\", value);")]
    [InlineData("var text = $\"failed {value}\";")]
    public void The_pattern_leaves_placeholder_templates_alone(string code) =>
        Assert.DoesNotMatch(InterpolatedLogCall(), code);

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AqwCompanion.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("AqwCompanion.slnx not found above " + AppContext.BaseDirectory);
    }
}
