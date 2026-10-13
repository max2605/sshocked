using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MonitoringScheduleTests
{
    [Theory]
    [InlineData(60, 0, 60)]
    [InlineData(60, 1, 120)]
    [InlineData(60, 2, 240)]
    [InlineData(60, 10, 900)]
    [InlineData(3600, 0, 3600)]
    [InlineData(3600, 1, 3600)]
    [InlineData(21600, 10, 21600)]
    public void FailedProbesBackOffWithoutShorteningTheConfiguredInterval(int interval, int failures, int expected)
    {
        var record = new MonitoringRecord { State = { [MonitoringStateKeys.Failures] = failures.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        Assert.Equal(TimeSpan.FromSeconds(expected), MonitoringSchedule.Delay(new() { IntervalSeconds = interval }, record));
    }

    [Fact]
    public async Task WatchRequiresExplicitEnablementAndDisposedServicesStartNoProbes()
    {
        using var fixture = new MonitoringTestFixture();
        fixture.Add();
        fixture.Repository.Update(config => config.Monitoring.Enabled = false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.Watch(_ => { }));
        fixture.Service.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.RunOne(fixture.Service.Definitions()[0].Id));
        Assert.Equal(0, fixture.Provider.Calls);
    }
}
