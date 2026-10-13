using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

internal sealed class MonitoringTestFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "sshocked-monitoring-tests-" + Guid.NewGuid().ToString("N"));
    public JsonConfigRepository Repository { get; }
    public MonitoringStore Store { get; }
    public MonitoringTestProvider Provider { get; } = new();
    public MonitoringTestClock Clock { get; } = new();
    public MacroTestDependencies Audit { get; } = new();
    public MonitoringService Service { get; }
    public MonitoringTestFixture()
    {
        Repository = new(Path.Combine(DirectoryPath, "appconfig.json"));
        Repository.Update(config =>
        {
            config.Hosts.Add(new() { Id = "host", Alias = "web", HostName = "host.invalid", User = "user" });
            config.Monitoring.Enabled = true;
            config.Monitoring.MaxConcurrency = 2;
        });
        Store = new(Path.Combine(DirectoryPath, "monitoring"));
        Service = new(Repository, [Provider], Store, Audit, new MonitorAlertEvaluator(), Clock);
    }
    public MonitorDefinition Add(string name = "server", string hostId = "host")
    {
        var definition = new MonitorDefinition { Name = name, HostId = hostId, Provider = "test" };
        Service.Save(definition);
        return definition;
    }
    public void Dispose()
    {
        Service.Dispose();
        var path = Path.GetFullPath(DirectoryPath);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "sshocked-monitoring-tests-", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
