using System.Globalization;

namespace Sshocked.Services;

public sealed class CommandOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positionals { get; } = [];

    public CommandOptions(string[] arguments, string[] valueOptions, string[]? flags = null)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal)) { Positionals.Add(argument); continue; }
            var key = argument[2..];
            if (_values.ContainsKey(key)) throw new ArgumentException($"Option --{key} was supplied twice.");
            if (flags?.Contains(key, StringComparer.OrdinalIgnoreCase) == true) { _values.Add(key, null); continue; }
            if (!valueOptions.Contains(key, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"Unknown option --{key}.");
            if (++i >= arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Option --{key} requires a value.");
            _values.Add(key, arguments[i]);
        }
    }

    public bool Has(string name) => _values.ContainsKey(name);
    public string? Get(string name, string? fallback = null) => _values.GetValueOrDefault(name, fallback);
    public int Integer(string name, int fallback, int minimum = 1, int maximum = 100000)
    {
        var raw = Get(name);
        if (raw is null) return fallback;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw new ArgumentException($"--{name} must be an integer from {minimum} to {maximum}.");
        return value;
    }
    public DateTimeOffset? Date(string name)
    {
        var raw = Get(name);
        if (raw is null) return null;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            throw new ArgumentException($"--{name} must be an ISO date or timestamp.");
        return value.ToUniversalTime();
    }
}
