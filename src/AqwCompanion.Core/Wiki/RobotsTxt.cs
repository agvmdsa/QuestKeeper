using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Wiki;

/// <summary>
/// The rules of a robots.txt that apply to this app: the group for the exact <c>AqwCompanion</c> agent when present,
/// otherwise the <c>*</c> group. Patterns support <c>*</c> and a trailing <c>$</c>; the longest matching rule wins and
/// Allow wins a tie. Without a matching group everything is allowed.
/// </summary>
public sealed class RobotsTxt
{
    public const string AgentName = "AqwCompanion";

    private readonly IReadOnlyList<(bool Allow, string Pattern)> _rules;

    private RobotsTxt(IReadOnlyList<(bool Allow, string Pattern)> rules) => _rules = rules;

    public static RobotsTxt Parse(string? text)
    {
        var specific = new List<(bool, string)>();
        var general = new List<(bool, string)>();
        var hasSpecific = false;
        var agents = new List<string>();
        var inRules = false;

        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var field = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (field.EqualsIgnoreCase("user-agent"))
            {
                if (inRules)
                {
                    agents.Clear();
                    inRules = false;
                }

                agents.Add(value);
                hasSpecific |= value.EqualsIgnoreCase(AgentName);
            }
            else if (field.EqualsIgnoreCase("disallow") || field.EqualsIgnoreCase("allow"))
            {
                inRules = true;
                if (value.Length == 0)
                {
                    continue;
                }

                var rule = (field.EqualsIgnoreCase("allow"), value);
                if (agents.Any(agent => agent.EqualsIgnoreCase(AgentName)))
                {
                    specific.Add(rule);
                }

                if (agents.Contains("*", StringComparer.Ordinal))
                {
                    general.Add(rule);
                }
            }
        }

        return new RobotsTxt(hasSpecific ? specific : general);
    }

    /// <summary>True when the path (with its query, if any) may be fetched.</summary>
    public bool IsAllowed(string path)
    {
        var target = string.IsNullOrEmpty(path) ? "/" : path;
        var bestLength = -1;
        var allowed = true;
        foreach (var (allow, pattern) in _rules)
        {
            if (!Matches(pattern, target))
            {
                continue;
            }

            if (pattern.Length > bestLength || (pattern.Length == bestLength && allow))
            {
                bestLength = pattern.Length;
                allowed = allow;
            }
        }

        return allowed;
    }

    private static bool Matches(string pattern, string path)
    {
        var anchored = pattern.EndsWith('$');
        if (anchored)
        {
            pattern = pattern[..^1];
        }

        var parts = pattern.Split('*');
        var position = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == 0)
            {
                if (!path.StartsWith(part, StringComparison.Ordinal))
                {
                    return false;
                }

                position = part.Length;
            }
            else if (i == parts.Length - 1 && anchored)
            {
                if (path.Length - part.Length < position || !path.EndsWith(part, StringComparison.Ordinal))
                {
                    return false;
                }

                position = path.Length;
            }
            else
            {
                var index = path.IndexOf(part, position, StringComparison.Ordinal);
                if (index < 0)
                {
                    return false;
                }

                position = index + part.Length;
            }
        }

        return !anchored || position == path.Length;
    }
}
