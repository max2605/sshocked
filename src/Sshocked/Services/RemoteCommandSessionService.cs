using System.Diagnostics;
using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class RemoteCommandSessionService(IProcessService processService, IExternalProcessRunner runner,
    IAuditService audit, IConfigRepository repository, ISessionRecordingService recording, SessionRepository sessions) : IRemoteCommandSessionService
{
    public async Task<IManagedCommandSession> Open(ServerHost host, CancellationToken cancellationToken = default)
    {
        if (host.AzureBastion?.AuthType == BastionAuthType.Aad && host.KeySecretRef is null && host.PasswordSecretRef is null)
            throw new NotSupportedException("Managed command sessions need an SSH key or password through a Bastion tunnel. Native AAD sessions use Connect.");
        var lease = await processService.PrepareConnection(host, cancellationToken);
        Process? process = null;
        SessionWriter? writer = null;
        RemoteCommandSession? session = null;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = SshConnectionConstants.Executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var arguments = SshArguments.Build(lease.Host, command: "sh").ToList();
            arguments.Insert(arguments.IndexOf("--"), "-T");
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            foreach (var variable in lease.Environment) info.Environment[variable.Key] = variable.Value;
            cancellationToken.ThrowIfCancellationRequested();
            if (recording.ShouldRecord(host))
            {
                var settings = repository.Load().SessionRecording;
                sessions.ApplyRetention(settings);
                writer = sessions.Create(host, settings.CaptureTiming);
            }
            process = runner.Start(info);
            session = new RemoteCommandSession(process, lease, host, audit, repository, sessions, writer, cancellationToken);
            session.StartReaders();
            await process.StandardInput.WriteAsync("exec 9>&1 8>&2\n".AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            // Finish authentication before exposing a local command prompt. Otherwise a manual
            // SSH password prompt could compete with the input that macro recording captures.
            await session.Initialize(cancellationToken);
            audit.Record(new()
            {
                Category = "connection",
                EventType = "connection.command-session.start",
                HostId = host.Id,
                Alias = host.Alias,
                Action = "Managed command session"
            });
            return session;
        }
        catch
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch { }
                throw;
            }
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                process.Dispose();
            }
            writer?.Dispose();
            await lease.DisposeAsync();
            throw;
        }
    }
}
