using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

public static class ForwardingDisplay
{
    public static string Format(IReadOnlyList<ForwardingStatus> status, string format) => format.ToLowerInvariant() switch
    {
        "json" => JsonSerializer.Serialize(status.ToList(), ForwardingJsonContext.Default.ListForwardingStatus),
        "table" => string.Join(Environment.NewLine, status.Select(item =>
            $"{BatchReportFormatter.Safe(item.Name)} ({item.ProfileId}): {item.State}; {BatchReportFormatter.Safe(item.Route)}{(item.Error is null ? "" : "; " + BatchReportFormatter.Safe(item.Error))}")),
        _ => throw new ArgumentException("Use --output table|json.")
    };
}
