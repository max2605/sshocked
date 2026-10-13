using System.Globalization;
using System.Text.RegularExpressions;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public static class MonitoringDisplay
{
    public static string Literal(string? value) => Regex.Replace(value ?? "", @"[\x00-\x1F\x7F-\x9F]", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static void Write(IConsoleWriterService console, IReadOnlyList<MonitorSnapshot> snapshots)
    {
        if (snapshots.Count == 0) { console.WriteLine("No monitors match this selection."); return; }
        foreach (var snapshot in snapshots)
        {
            console.WriteLine($"{Literal(snapshot.MonitorId)} [{snapshot.Severity}/{snapshot.Quality}] host={Literal(snapshot.HostId)} attempt={snapshot.ObservedUtc:O} success={snapshot.LastSuccessUtc:O}");
            foreach (var metric in snapshot.Metrics)
                console.WriteLine($"  {Literal(metric.Key)}: {metric.NumericValue?.ToString("0.###", CultureInfo.InvariantCulture) ?? Literal(metric.TextValue ?? "unavailable")} {Literal(metric.Unit)} [{metric.Quality}] sample={metric.SampledUtc:O}");
            if (snapshot.Message is { } message) console.WriteLine("  " + Literal(message));
        }
    }

    public static int ExitCode(IReadOnlyList<MonitorSnapshot> snapshots) =>
        snapshots.Count == 0 || snapshots.Any(item => item.Quality is not (MonitorDataQuality.Success or MonitorDataQuality.Disabled)) ? 2
        : snapshots.Any(item => item.Severity is MonitorSeverity.Warning or MonitorSeverity.Critical) ? 1 : 0;
}
