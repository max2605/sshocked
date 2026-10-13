using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

internal sealed class MonitoringTestProvider : IMonitoringProvider
{
    private int active;
    public string Key => "test";
    public string Kind => "server";
    public int MaximumActive { get; private set; }
    public int Calls { get; private set; }
    public IReadOnlyList<MonitorOption> Options => [];
    public Func<ServerHost, CancellationToken, Task<MonitoringObservation>> Work { get; set; } =
        (_, _) => Task.FromResult(Value(80));
    public Action<MonitoringRecord?>? InspectPrevious { get; set; }
    private readonly object gate = new();
    public void Validate(MonitorDefinition definition) { }
    public async Task<MonitoringObservation> Collect(ServerHost host, MonitorDefinition definition, MonitoringRecord? previous, CancellationToken cancellationToken = default)
    {
        lock (gate) { Calls++; active++; MaximumActive = Math.Max(MaximumActive, active); }
        try { InspectPrevious?.Invoke(previous); return await Work(host, cancellationToken); }
        finally { lock (gate) active--; }
    }
    public static MonitoringObservation Value(double? value, MonitorDataQuality quality = MonitorDataQuality.Success) =>
        new() { Snapshot = new() { Quality = quality, Metrics = [new() { Key = "cpu.percent", NumericValue = value, Quality = value is null ? MonitorDataQuality.Unsupported : quality }] } };
}
