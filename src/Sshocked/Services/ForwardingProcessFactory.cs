using System.Diagnostics;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class ForwardingProcessFactory(IExternalProcessRunner runner) : IForwardingProcessFactory
{
    public IForwardingProcess Start(ProcessStartInfo info, ForwardingProfile profile)
    {
        var child = runner.Start(info);
        try { return new ForwardingProcess(child, profile.Kind, profile.ListenPort); }
        catch
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            child.WaitForExit(5000);
            child.Dispose();
            throw;
        }
    }
}
