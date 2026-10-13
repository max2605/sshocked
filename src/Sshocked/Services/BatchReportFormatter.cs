using System.Globalization;
using System.Text;
using System.Text.Json;
using Sshocked.Models;

namespace Sshocked.Services;

public static class BatchReportFormatter
{
    public static string Format(BatchReport report, string format) => format.ToLowerInvariant() switch
    {
        "json" => JsonSerializer.Serialize(report, BatchJsonContext.Default.BatchReport),
        "csv" => Csv(report),
        "table" => string.Join(Environment.NewLine, report.Results.Select(result =>
            $"{Safe(result.Alias)}: {result.Status}; {result.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture)}s; exit {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}{(result.Error is null ? "" : "; " + Safe(result.Error))}")),
        _ => throw new ArgumentException("Use --output table|json|csv.")
    };

    public static BatchReport Read(string path) => JsonSerializer.Deserialize(File.ReadAllText(path), BatchJsonContext.Default.BatchReport)
        ?? throw new ArgumentException("The batch report is invalid.");

    public static void Write(BatchReport report, string path)
    {
        path = Path.GetFullPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, Format(report, "json"));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string Safe(string value) => string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Csv(BatchReport report)
    {
        var builder = new StringBuilder("hostId,alias,status,durationSeconds,exitCode,error\n");
        foreach (var result in report.Results)
            builder.AppendLine(string.Join(',', new[] { result.HostId, result.Alias, result.Status.ToString(),
                result.DurationSeconds.ToString("F6", CultureInfo.InvariantCulture), result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "", result.Error ?? "" }.Select(Cell)));
        return builder.ToString();
    }

    private static string Cell(string value)
    {
        value = Safe(value);
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
