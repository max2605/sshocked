using Spectre.Console;

namespace Sshocked.Services;

/// <summary>Semantic colors keep information readable against the selected terminal background.</summary>
public sealed record UiThemePalette(string Name, string Text, string Background, string Accent,
    string Muted, string Success, string Warning, string Error, string Selection)
{
    public Style SelectionStyle => Style.Parse(Selection);
    public Style BodyStyle => Style.Parse($"{Text} on {Background}");

    public static UiThemePalette For(string? theme) => string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
        ? new("Light", "black", "white", "blue", "grey35", "darkgreen", "orange4", "maroon", "white on blue bold")
        : new("Dark", "silver", "black", "cyan", "grey70", "green3", "yellow", "red", "black on cyan bold");

    public string AdaptMarkup(string markup)
    {
        // Adapt only style tags: escaped host names such as [[green]] must remain literal text.
        return System.Text.RegularExpressions.Regex.Replace(markup, @"(?<!\[)\[(?!\[)([^\]]+)\](?!\])", match =>
        {
            if (match.Groups[1].Value.StartsWith('/')) return match.Value;
            var parts = match.Groups[1].Value.Split(' ');
            for (var index = 0; index < parts.Length; index++)
                parts[index] = parts[index] switch
                {
                    "cyan" or "teal" or "blue" => Accent,
                    "grey" or "grey35" => Muted,
                    "green" => Success,
                    "yellow" or "darkgoldenrod" => Warning,
                    "red" => Error,
                    _ => parts[index]
                };
            return "[" + string.Join(' ', parts) + "]";
        });
    }
}
