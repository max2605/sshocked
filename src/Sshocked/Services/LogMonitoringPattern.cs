using System.Text.RegularExpressions;
using Sshocked.Models;

namespace Sshocked.Services;

internal sealed class LogMonitoringPattern(MonitorDefinition definition)
{
    private readonly string mode = MonitoringOptions.Get(definition, "patternMode", "all");
    private readonly string pattern = MonitoringOptions.Get(definition, "pattern");
    private readonly bool ignoreCase = MonitoringOptions.Boolean(definition, "ignoreCase");
    private readonly Regex? regex = MonitoringOptions.Get(definition, "patternMode", "all") == "regex" ? CreateRegex(definition) : null;

    public bool Matches(string line) => mode switch
    {
        "all" => true,
        "literal" => line.Contains(pattern, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
        "regex" => regex!.IsMatch(line),
        _ => throw new ArgumentException("Pattern mode must be all, literal or regex.")
    };

    private static Regex CreateRegex(MonitorDefinition definition) => new(MonitoringOptions.Get(definition, "pattern"),
        RegexOptions.CultureInvariant | (MonitoringOptions.Boolean(definition, "ignoreCase") ? RegexOptions.IgnoreCase : RegexOptions.None),
        TimeSpan.FromMilliseconds(MonitoringOptions.Integer(definition, "regexTimeoutMs", 100, 10, 500)));
}
