using System.Net;
using System.Net.Sockets;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ForwardingTests
{
    [Theory]
    [InlineData(ForwardingKind.Local, "-L", "127.0.0.1:1234:private.example:5432")]
    [InlineData(ForwardingKind.Remote, "-R", "127.0.0.1:1234:private.example:5432")]
    [InlineData(ForwardingKind.Dynamic, "-D", "127.0.0.1:1234")]
    public void ForwardingArgumentsUseLiteralTokensAndRequiredSafetyOptions(ForwardingKind kind, string flag, string route)
    {
        var host = new ServerHost { Id = "server", HostName = "literal.example", User = "tester", IdentityFile = "key with spaces", AuthType = AuthType.SshKey };
        var profile = Profile(kind);
        var info = ForwardingArguments.Build(host, profile, new Dictionary<string, string?>());
        var arguments = info.ArgumentList.ToList();
        Assert.Equal(route, arguments[arguments.IndexOf(flag) + 1]);
        Assert.Contains("-N", arguments);
        Assert.Contains("ExitOnForwardFailure=yes", arguments);
        Assert.Contains("StrictHostKeyChecking=yes", arguments);
        Assert.Contains("ForkAfterAuthentication=no", arguments);
        Assert.Contains("none", arguments);
        Assert.Contains("key with spaces", arguments);
        Assert.Equal("tester@literal.example", arguments.Last());
        Assert.True(info.RedirectStandardInput);
    }

    [Fact]
    public void ExternalBindingsRequireExplicitChoiceAndInvalidPortsOrSelectorsAreRejected()
    {
        var profile = Profile();
        profile.BindAddress = "0.0.0.0";
        Assert.Throws<ArgumentException>(() => ForwardingValidation.Validate(profile));
        profile.AllowNonLoopbackBind = true;
        ForwardingValidation.Validate(profile);
        profile.ListenPort = 0;
        Assert.Throws<ArgumentException>(() => ForwardingValidation.Validate(profile));
        profile.ListenPort = 1234;
        profile.TargetHost = "host;touch injected";
        Assert.Throws<ArgumentException>(() => ForwardingValidation.Validate(profile));
        Assert.Throws<ArgumentException>(() => ForwardingValidation.ValidateId("../escape"));
        profile.TargetHost = "::1";
        profile.BindAddress = "::1";
        var info = ForwardingArguments.Build(new() { HostName = "host", User = "user" }, profile, new Dictionary<string, string?>());
        Assert.Contains("[::1]:1234:[::1]:5432", info.ArgumentList);
    }

    [Fact]
    public void CustomOptionsCannotAddListenersOrDetachManagedProcesses()
    {
        foreach (var option in new[] { "-f", "-L 9999:host:22", "-o LocalForward=9999:host:22", "-E log.txt", "-M" })
            Assert.Throws<ArgumentException>(() => ForwardingArguments.Build(new() { HostName = "host", User = "user", CustomSshOptions = option }, Profile(), new Dictionary<string, string?>()));
    }

    [Theory]
    [InlineData(ForwardingKind.Local, "localforward ")]
    [InlineData(ForwardingKind.Remote, "remoteforward ")]
    [InlineData(ForwardingKind.Dynamic, "dynamicforward ")]
    public async Task OpenSshConfigDumpPreservesRequestedForwardingWithoutConnecting(ForwardingKind kind, string key)
    {
        var profile = Profile(kind);
        var info = ForwardingArguments.Build(new() { HostName = "localhost", User = "tester" }, profile, new Dictionary<string, string?>());
        info.ArgumentList.Insert(info.ArgumentList.IndexOf("--"), "-G");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ProcessResult result;
        try { result = await new ExternalProcessRunner().Run(info, timeout.Token); }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 2) { return; }
        Assert.True(result.ExitCode == 0, result.StandardError);
        var forwarding = result.StandardOutput.Split('\n').Single(line => line.StartsWith(key, StringComparison.Ordinal));
        Assert.Contains("127.0.0.1:1234", forwarding.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal));
        Assert.Contains("clearallforwardings no", result.StandardOutput);
        if (kind != ForwardingKind.Dynamic)
        {
            Assert.Contains("private.example", forwarding);
            Assert.Contains("5432", forwarding);
        }
    }

    [Fact]
    public async Task ManagedStartStopAndDisposeReleaseProcessesAndCredentials()
    {
        var fake = new ForwardingTestDependencies();
        var directory = TemporaryDirectory();
        try
        {
            await using var service = new PortForwardingService(fake, fake, fake, fake, new(directory));
            var profile = Profile(ForwardingKind.Remote);
            service.Save(profile);
            var status = await service.Start(profile.Id);
            Assert.Equal(ForwardingState.Active, status.State);
            Assert.False(fake.Leases.Single().IsDisposed);
            Assert.Throws<InvalidOperationException>(() => service.Delete(profile.Id));
            await service.Stop(profile.Id);
            Assert.True(fake.Processes.Single().IsDisposed);
            Assert.True(fake.Leases.Single().IsDisposed);
            Assert.Equal(ForwardingState.Stopped, service.Status().Single().State);
            await service.Start(profile.Id);
            await service.DisposeAsync();
            Assert.All(fake.Leases, lease => Assert.True(lease.IsDisposed));
            Assert.All(fake.Processes, process => Assert.True(process.IsDisposed));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task IndependentProcessFailureDoesNotStopOtherProfiles()
    {
        var fake = new ForwardingTestDependencies();
        var directory = TemporaryDirectory();
        try
        {
            await using var service = new PortForwardingService(fake, fake, fake, fake, new(directory));
            var first = Profile(ForwardingKind.Remote);
            var second = Profile(ForwardingKind.Remote);
            second.Name = "other";
            second.ListenPort++;
            service.Save(first);
            service.Save(second);
            await service.Start(first.Id);
            await service.Start(second.Id);
            fake.Processes[0].Fail();
            await service.Wait(first.Id);
            Assert.Equal(ForwardingState.Failed, service.Status().Single(status => status.ProfileId == first.Id).State);
            Assert.Equal(ForwardingState.Active, service.Status().Single(status => status.ProfileId == second.Id).State);
            Assert.False(fake.Leases[1].IsDisposed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task StartupCancellationAndHostkeyFailureNeverLeakLeases()
    {
        var fake = new ForwardingTestDependencies { CreateProcess = () => new(false) };
        var directory = TemporaryDirectory();
        try
        {
            await using var service = new PortForwardingService(fake, fake, fake, fake, new(directory));
            var profile = Profile(ForwardingKind.Remote);
            service.Save(profile);
            using var cancellation = new CancellationTokenSource();
            var startup = service.Start(profile.Id, cancellation.Token);
            await fake.Started.Task;
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
            Assert.True(fake.Leases[0].IsDisposed);
            Assert.True(fake.Processes[0].IsDisposed);
            var startupFailure = new ForwardingFakeProcess(false);
            startupFailure.FailStartup();
            fake.CreateProcess = () => startupFailure;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Start(profile.Id));
            Assert.True(fake.Leases[1].IsDisposed);
            Assert.True(startupFailure.IsDisposed);
            Assert.Equal(ForwardingState.Failed, service.Status().Single().State);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LocalPortConflictIsReportedBeforeCredentialsOrChildStart()
    {
        var fake = new ForwardingTestDependencies();
        var directory = TemporaryDirectory();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            await using var service = new PortForwardingService(fake, fake, fake, fake, new(directory));
            var profile = Profile();
            profile.ListenPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            service.Save(profile);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Start(profile.Id));
            Assert.Empty(fake.Leases);
            Assert.Empty(fake.Processes);
            Assert.Equal(ForwardingState.Failed, service.Status().Single().State);
        }
        finally { listener.Stop(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AnotherServiceCanRequestStopWithoutKillingAnUnverifiedPid()
    {
        var fake = new ForwardingTestDependencies();
        var directory = TemporaryDirectory();
        try
        {
            await using var owner = new PortForwardingService(fake, fake, fake, fake, new(directory));
            await using var client = new PortForwardingService(fake, fake, fake, fake, new(directory));
            var profile = Profile(ForwardingKind.Remote);
            owner.Save(profile);
            await owner.Start(profile.Id);
            Assert.Equal(ForwardingState.Active, client.Status().Single().State);
            await client.Stop(profile.Id);
            Assert.Equal(ForwardingState.Stopped, owner.Status().Single().State);
            Assert.True(fake.Processes.Single().IsDisposed);
            Assert.True(fake.Leases.Single().IsDisposed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CliCanCreateAndEditDynamicLoopbackProfilesWithoutRemoteWork()
    {
        var fake = new ForwardingTestDependencies();
        var directory = TemporaryDirectory();
        try
        {
            await using var service = new PortForwardingService(fake, fake, fake, fake, new(directory));
            var commands = new PortForwardingCommandService(service, fake, fake);
            await commands.Execute("forward", ["create", "socks", "--server", "server", "--kind", "dynamic", "--listen-port", "1080"]);
            var profile = service.Find("socks");
            Assert.Equal("127.0.0.1", profile.BindAddress);
            Assert.Equal(ForwardingKind.Dynamic, profile.Kind);
            await commands.Execute("forward", ["edit", "socks", "--listen-port", "1081"]);
            Assert.Equal(1081, service.Find("socks").ListenPort);
            Assert.Empty(fake.Processes);
            await commands.Execute("forward", ["delete", "socks", "--force"]);
            Assert.Empty(service.List());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ForwardingProfile Profile(ForwardingKind kind = ForwardingKind.Local) => new()
    { Name = "database", HostId = "server", Kind = kind, ListenPort = 1234, TargetHost = "private.example", TargetPort = 5432 };
    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ssk-forward-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
