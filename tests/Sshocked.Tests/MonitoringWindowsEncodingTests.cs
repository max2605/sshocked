using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringWindowsEncodingTests
{
    [Fact]
    public async Task EncodedPowerShellPreservesUnicodeJsonInUtf8Output()
    {
        if (!OperatingSystem.IsWindows()) return;
        var arguments = SshArguments.Tokenize(MonitoringOptions.PowerShellScript("[pscustomobject]@{name='Grüße 東京'}|ConvertTo-Json -Compress"));
        var info = new ProcessStartInfo(arguments[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments.Skip(1)) info.ArgumentList.Add(argument);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await new ExternalProcessRunner().RunBounded(info, 4096, cancellationToken: cancellation.Token);
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("Grüße 東京", document.RootElement.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("{\"resultCode\":null,\"updates\":null,\"reboot\":null}")]
    [InlineData("{\"resultCode\":2,\"updates\":0,\"reboot\":null}")]
    public void MissingWindowsUpdateFieldsRemainPartial(string json)
    {
        var observation = UpdateMonitoringProvider.ParseWindows(json, new());
        Assert.Equal(MonitorDataQuality.Partial, observation.Snapshot.Quality);
        Assert.Null(observation.Conditions.Single(condition => condition.Key == "updates.reboot").Triggered);
    }
}
