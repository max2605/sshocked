using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ForwardingProcessTests
{
    [Fact]
    public async Task PreBindDebugLineDoesNotClaimAnActiveListener()
    {
        if (!File.Exists(LocalShellTestRunner.ShellPath)) return;
        var info = LocalShellTestRunner.ShellInfo("printf '%s\n' 'Authenticated to host using publickey.' 'debug1: Local forwarding listening on 127.0.0.1 port 1234.' >&2; sleep 60");
        info.RedirectStandardInput = true;
        var child = new ExternalProcessRunner().Start(info);
        await using var forwarding = new ForwardingProcess(child, ForwardingKind.Local, 1234);
        await Assert.ThrowsAsync<TimeoutException>(() => forwarding.WaitForReady().WaitAsync(TimeSpan.FromMilliseconds(300)));
        await forwarding.Stop();
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task SuccessfulListenerChannelAndAuthenticationEstablishReadiness()
    {
        if (!File.Exists(LocalShellTestRunner.ShellPath)) return;
        var info = LocalShellTestRunner.ShellInfo("printf '%s\n' 'Authenticated to host using publickey.' 'debug1: Local forwarding listening on 127.0.0.1 port 1234.' 'debug1: channel 0: new port-listener [port listener]' >&2; sleep 60");
        info.RedirectStandardInput = true;
        var child = new ExternalProcessRunner().Start(info);
        await using var forwarding = new ForwardingProcess(child, ForwardingKind.Local, 1234);
        await forwarding.WaitForReady().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(child.HasExited);
        await forwarding.Stop();
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task RemoteForwardRequiresMatchingAcknowledgedPort()
    {
        if (!File.Exists(LocalShellTestRunner.ShellPath)) return;
        var info = LocalShellTestRunner.ShellInfo("printf '%s\n' 'Authenticated to host using publickey.' 'debug1: remote forward success for: listen 127.0.0.1:1235, connect target:22' >&2; sleep 60");
        info.RedirectStandardInput = true;
        var child = new ExternalProcessRunner().Start(info);
        await using var forwarding = new ForwardingProcess(child, ForwardingKind.Remote, 1234);
        await Assert.ThrowsAsync<TimeoutException>(() => forwarding.WaitForReady().WaitAsync(TimeSpan.FromMilliseconds(300)));
        await forwarding.Stop();
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task ExcessDiagnosticOutputStopsTheChildAndIsObserved()
    {
        if (!File.Exists(LocalShellTestRunner.ShellPath)) return;
        var info = LocalShellTestRunner.ShellInfo("awk 'BEGIN { for(i=0;i<40000;i++) print \"0123456789012345678901234567890123456789\" }'; sleep 60");
        info.RedirectStandardInput = true;
        var child = new ExternalProcessRunner().Start(info);
        var forwarding = new ForwardingProcess(child, ForwardingKind.Local, 1234);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => forwarding.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(child.HasExited);
        }
        finally
        {
            try { await forwarding.DisposeAsync(); }
            catch (InvalidDataException) { child.Dispose(); }
        }
    }

    [Fact]
    public async Task ActiveForwardingDrainsMoreThanStartupBudgetAndKeepsItsFailureDiagnostic()
    {
        if (!File.Exists(LocalShellTestRunner.ShellPath)) return;
        var info = LocalShellTestRunner.ShellInfo("printf '%s\n' 'Authenticated to host using publickey.' 'debug1: Local forwarding listening on 127.0.0.1 port 1234.' 'debug1: channel 0: new port-listener [port listener]' >&2; sleep 0.3; awk 'BEGIN { for(i=0;i<40000;i++) print \"debug1: ordinary traffic after a successful listener setup\" }' >&2; printf '%s\n' 'client_loop: send disconnect: Broken pipe' >&2; exit 17");
        info.RedirectStandardInput = true;
        var child = new ExternalProcessRunner().Start(info);
        await using var forwarding = new ForwardingProcess(child, ForwardingKind.Local, 1234);
        await forwarding.WaitForReady().WaitAsync(TimeSpan.FromSeconds(5));
        await forwarding.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(17, forwarding.ExitCode);
        Assert.Equal("The SSH forwarding connection was interrupted.", forwarding.Error);
    }
}
