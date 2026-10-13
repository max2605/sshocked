using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class PortForwardingService(IConfigRepository repository, IProcessService connections,
    IForwardingProcessFactory processes, IAuditService audit, ForwardingRuntimeStore runtime) : IPortForwardingService
{
    private readonly ConcurrentDictionary<string, ForwardingSession> _sessions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public IReadOnlyList<ForwardingProfile> List() => repository.Load().ForwardingProfiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public ForwardingProfile Find(string nameOrId)
    {
        var matches = List().Where(profile => profile.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) || profile.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw new ArgumentException("Forwarding profile not found or ambiguous; use its ID.");
    }

    public void Save(ForwardingProfile profile, ForwardingProfile? expected = null)
    {
        profile.Name = profile.Name.Trim();
        ForwardingValidation.Validate(profile);
        using var profileLease = runtime.Acquire(profile.Id);
        RequireStopped(profile.Id);
        var frozen = Clone(profile);
        repository.Update(config =>
        {
            if (!config.Hosts.Any(host => host.Id == frozen.HostId)) throw new ArgumentException("The selected server no longer exists.");
            if (config.ForwardingProfiles.Any(existing => existing.Id != frozen.Id && existing.Name.Equals(frozen.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A forwarding profile with that name already exists.");
            var current = config.ForwardingProfiles.FirstOrDefault(existing => existing.Id == frozen.Id);
            if (current is not null && expected is null) throw new InvalidOperationException("Editing requires the expected profile version.");
            if (expected is not null && (current is null || Serialize(current) != Serialize(expected))) throw new InvalidOperationException("The forwarding profile changed. Reload it before editing.");
            config.ForwardingProfiles.RemoveAll(existing => existing.Id == frozen.Id);
            config.ForwardingProfiles.Add(frozen);
        });
        audit.Record(new() { Category = "config", EventType = "forwarding.saved", HostId = profile.HostId, Action = profile.Name });
    }

    public void Delete(string nameOrId)
    {
        var profile = Find(nameOrId);
        using var profileLease = runtime.Acquire(profile.Id);
        RequireStopped(profile.Id);
        repository.Update(config => config.ForwardingProfiles.RemoveAll(existing => existing.Id == profile.Id));
        audit.Record(new() { Category = "config", EventType = "forwarding.deleted", HostId = profile.HostId, Action = profile.Name });
    }

    public IReadOnlyList<ForwardingStatus> Status() => List().Select(profile =>
    {
        var status = runtime.Read(profile.Id)?.Status ?? new() { ProfileId = profile.Id, Name = profile.Name, HostId = profile.HostId, Route = ForwardingValidation.Route(profile) };
        if (status.State is ForwardingState.Active or ForwardingState.Starting && !ForwardingRuntimeStore.OwnerAlive(status))
        { status.State = ForwardingState.Failed; status.Error = "The owning sshocked process has ended; listener activity cannot be confirmed."; }
        return status;
    }).ToList();

    public async Task<ForwardingStatus> Start(string nameOrId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var profile = Clone(Find(nameOrId));
        ForwardingValidation.Validate(profile);
        var host = repository.Load().Hosts.FirstOrDefault(host => host.Id == profile.HostId) ?? throw new ArgumentException("The selected server no longer exists.");
        if (host.AzureBastion?.AuthType == BastionAuthType.Aad && host.KeySecretRef is null && host.PasswordSecretRef is null)
            throw new NotSupportedException("Forwarding profiles require a key/password SSH connection or Bastion tunnel.");
        var profileLease = runtime.Acquire(profile.Id);
        ISshConnectionLease? credentials = null;
        IForwardingProcess? process = null;
        ForwardingSession? session = null;
        var owner = ForwardingRuntimeStore.CurrentOwner();
        var record = new ForwardingRuntimeRecord
        {
            Status = new()
            {
                ProfileId = profile.Id,
                Name = profile.Name,
                HostId = profile.HostId,
                Route = ForwardingValidation.Route(profile),
                State = ForwardingState.Starting,
                OwnerProcessId = owner.Id,
                OwnerStartedUtc = owner.StartedUtc
            }
        };
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (profile.Kind != ForwardingKind.Remote) CheckLocalPort(profile);
            runtime.ClearStop(profile.Id);
            runtime.Write(record);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(profile.StartupTimeoutSeconds));
            credentials = await connections.PrepareConnection(host, startup.Token);
            process = processes.Start(ForwardingArguments.Build(credentials.Host, profile, credentials.Environment), profile);
            record.Status.SshProcessId = process.Id;
            record.Status.StartedUtc = DateTimeOffset.UtcNow;
            runtime.Write(record);
            session = new(record, process, credentials, profileLease, cancellation);
            if (!_sessions.TryAdd(profile.Id, session)) throw new InvalidOperationException("This forwarding profile is already active.");
            session.Completion = Manage(session);
            await process.WaitForReady(startup.Token);
            lock (session.Gate)
            {
                if (record.Status.State != ForwardingState.Starting) throw new InvalidOperationException("Forwarding ended while establishing its listener.");
                record.Status.State = ForwardingState.Active;
                runtime.Write(record);
            }
            audit.Record(new() { Category = "connection", EventType = "forwarding.started", HostId = host.Id, Alias = host.Alias, Action = profile.Name, Success = true });
            return record.Status;
        }
        catch (Exception exception)
        {
            var failure = exception is OperationCanceledException && cancellationToken.IsCancellationRequested ? null
                : exception is OperationCanceledException ? "Forwarding authentication/listener setup timed out." : SafeFailure(exception);
            if (session is not null)
            {
                if (!session.Completion.IsCompleted)
                {
                    lock (session.Gate) { session.Failure = failure; session.StopRequested = failure is null; }
                    try { cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                await session.Completion;
            }
            else
            {
                if (process is not null) await process.DisposeAsync();
                if (credentials is not null) await credentials.DisposeAsync();
                await profileLease.DisposeAsync();
                cancellation.Dispose();
                record.Status.State = failure is null ? ForwardingState.Stopped : ForwardingState.Failed;
                record.Status.Error = failure;
                runtime.Write(record);
            }
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
                throw new TimeoutException("Forwarding authentication/listener setup timed out.");
            throw;
        }
    }

    private async Task Manage(ForwardingSession session)
    {
        var status = session.Record.Status;
        var stop = runtime.WaitForStop(status.ProfileId, session.Record.ControlToken, session.Cancellation.Token);
        try
        {
            var completed = await Task.WhenAny(session.Process.Completion, stop);
            if (completed == stop)
            {
                try { await stop; }
                catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested) { }
                session.StopRequested = true;
                await session.Process.Stop();
            }
            else
            {
                await session.Process.Completion;
                if (session.Cancellation.IsCancellationRequested || session.StopRequested) session.StopRequested = true;
                else session.Failure ??= session.Process.Error ?? "The SSH forwarding process ended unexpectedly.";
            }
        }
        catch (Exception exception) { session.Failure ??= SafeFailure(exception); }
        finally
        {
            session.Cancellation.Cancel();
            try { await stop; }
            catch (OperationCanceledException) { }
            try { await session.Process.DisposeAsync(); }
            catch (Exception exception) { session.Failure ??= "Forwarding process cleanup failed: " + exception.GetType().Name; }
            try { await session.Credentials.DisposeAsync(); }
            catch (Exception exception) { session.Failure ??= "Credential cleanup failed: " + exception.GetType().Name; }
            try
            {
                lock (session.Gate)
                {
                    status.State = session.Failure is not null ? ForwardingState.Failed : session.StopRequested ? ForwardingState.Stopped : ForwardingState.Failed;
                    status.ExitCode = session.Process.ExitCode;
                    status.Error = session.Failure;
                    runtime.Write(session.Record);
                }
                runtime.ClearStop(status.ProfileId);
                audit.Record(new()
                {
                    Category = "connection",
                    EventType = "forwarding.ended",
                    HostId = status.HostId,
                    Action = status.Name,
                    Success = status.State == ForwardingState.Stopped,
                    ExitCode = status.ExitCode,
                    ErrorMessage = status.Error
                });
            }
            finally
            {
                await session.ProfileLease.DisposeAsync();
                _sessions.TryRemove(status.ProfileId, out _);
                session.Cancellation.Dispose();
            }
        }
    }

    public async Task Stop(string nameOrId, CancellationToken cancellationToken = default)
    {
        var profile = Find(nameOrId);
        if (_sessions.TryGetValue(profile.Id, out var session))
        {
            session.StopRequested = true;
            try { session.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            await session.Completion.WaitAsync(cancellationToken);
            return;
        }
        var record = runtime.Read(profile.Id);
        if (record is null || record.Status.State is not (ForwardingState.Starting or ForwardingState.Active) || !ForwardingRuntimeStore.OwnerAlive(record.Status)) return;
        runtime.RequestStop(record);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (runtime.Read(profile.Id)?.Status is { State: ForwardingState.Active or ForwardingState.Starting } current && ForwardingRuntimeStore.OwnerAlive(current))
                await Task.Delay(100, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The forwarding owner did not finish cleanup within 10 seconds."); }
    }

    public async Task Wait(string nameOrId, CancellationToken cancellationToken = default)
    {
        var profile = Find(nameOrId);
        if (_sessions.TryGetValue(profile.Id, out var session)) await session.Completion.WaitAsync(cancellationToken);
    }

    private void RequireStopped(string id)
    {
        var current = runtime.Read(id)?.Status;
        if (current?.State is ForwardingState.Starting or ForwardingState.Active && ForwardingRuntimeStore.OwnerAlive(current))
            throw new InvalidOperationException("Stop the forwarding profile before editing or deleting it.");
    }

    private static void CheckLocalPort(ForwardingProfile profile)
    {
        var address = ForwardingValidation.BindAddress(profile.BindAddress);
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        try { socket.Bind(new IPEndPoint(address, profile.ListenPort)); }
        catch (SocketException) { throw new InvalidOperationException("The local forwarding listener port is in use or its bind address is unavailable."); }
    }

    private static string SafeFailure(Exception exception) => exception is InvalidOperationException or NotSupportedException
        ? AuditRedactor.Redact(exception.Message) ?? exception.GetType().Name : exception.GetType().Name;
    private static string Serialize(ForwardingProfile profile) => JsonSerializer.Serialize(profile, ForwardingJsonContext.Default.ForwardingProfile);
    private static ForwardingProfile Clone(ForwardingProfile profile) => JsonSerializer.Deserialize(Serialize(profile), ForwardingJsonContext.Default.ForwardingProfile)!;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        await Task.WhenAll(_sessions.Values.Select(session => session.Completion));
        _lifetime.Dispose();
    }
}
