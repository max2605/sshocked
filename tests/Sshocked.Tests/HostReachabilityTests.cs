using System.Net;
using System.Net.Sockets;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class HostReachabilityTests
{
    [Fact]
    public async Task TcpCheckUsesLocalPortAndCachesResults()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var host = new ServerHost { Id = "local", HostName = "127.0.0.1", Port = port };
        var clock = new HostReachabilityTestsClock();
        using var service = new HostReachabilityService(clock);
        Assert.Equal(HostReachabilityState.Unchecked, service.GetStatus(host).State);
        Assert.Equal(HostReachabilityState.Online, (await service.CheckPortOpen(host)).State);
        listener.Stop();
        Assert.Equal(HostReachabilityState.Online, service.GetStatus(host).State);
        clock.Now = clock.Now.AddMinutes(6);
        Assert.Equal(HostReachabilityState.Stale, service.GetStatus(host).State);
        Assert.Equal(HostReachabilityState.Offline, (await service.CheckPortOpen(host)).State);
        Assert.False(service.GetStatus(host).IsReachable);
    }

    [Fact]
    public async Task AzureAndJumpHostRoutesDoNotProbePrivateEndpoints()
    {
        using var service = new HostReachabilityService();
        var host = new ServerHost { HostName = "private.invalid", AzureBastion = new() };
        Assert.Equal(HostReachabilityState.ViaBastion, (await service.CheckPortOpen(host)).State);
        host.AzureBastion = null;
        host.ProxyJump = "jump.example";
        Assert.Equal(HostReachabilityState.ViaJumpHost, service.GetStatus(host, scheduleCheck: true).State);
        host.ProxyJump = null;
        Assert.Equal(HostReachabilityState.Unchecked, service.GetStatus(host).State);
    }

    [Fact]
    public void ReachabilityChecksAreDisabledByDefault()
        => Assert.False(new UISettings().EnableReachabilityChecks);
}
