using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringPersistenceFailureTests
{
    [Fact]
    public async Task AnUnstoredFailureShadowsThePreviousOkSnapshotUntilAProbeSucceeds()
    {
        using var fixture = new MonitoringTestFixture();
        fixture.Add();
        var store = new MonitoringFaultStore(fixture.Store);
        using var service = new MonitoringService(fixture.Repository, [fixture.Provider], store, fixture.Audit, new(), fixture.Clock);
        await service.Run();
        Assert.Equal(MonitorSeverity.Ok, Assert.Single(service.Status()).Severity);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        store.FailSave = true;
        Assert.Equal(MonitorDataQuality.Error, Assert.Single(await service.Run()).Quality);
        var shown = Assert.Single(service.Status());
        Assert.Equal(MonitorDataQuality.Error, shown.Quality);
        Assert.Equal(MonitorSeverity.Unknown, shown.Severity);
        store.FailSave = false;
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await service.Run();
        Assert.Equal(MonitorSeverity.Ok, Assert.Single(service.Status()).Severity);
    }
}
