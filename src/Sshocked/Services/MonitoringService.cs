using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class MonitoringService(IConfigRepository repository, IEnumerable<IMonitoringProvider> providers,
    IMonitoringStore store, IAuditService audit, MonitorAlertEvaluator evaluator, TimeProvider clock) : IMonitoringService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _parallel = new(Math.Clamp(repository.Load().Monitoring.MaxConcurrency, 1, 4));
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _hosts = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MonitorSnapshot> _failures = new(StringComparer.Ordinal);
    public IReadOnlyList<IMonitoringProvider> Providers { get; } = providers.ToList();

    public IReadOnlyList<MonitorDefinition> Definitions() => repository.Load().Monitoring.Monitors;
    public MonitorDefinition Find(string idOrName)
    {
        var matches = Definitions().Where(item => item.Id == idOrName || item.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw new ArgumentException("Monitor not found or name is ambiguous; use its ID.");
    }

    private IMonitoringProvider Provider(MonitorDefinition definition) => Providers.FirstOrDefault(item => item.Key == definition.Provider)
        ?? throw new NotSupportedException("This monitoring provider is unavailable.");

    public void Save(MonitorDefinition definition, MonitorDefinition? expected = null)
    {
        definition.Name = definition.Name.Trim();
        MonitoringValidation.Validate(definition);
        var provider = Provider(definition);
        if (definition.Options.Keys.Any(key => !provider.Options.Any(option => option.Key.Equals(key, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("An option is not supported by the selected provider.");
        provider.Validate(definition);
        var frozen = Clone(definition);
        repository.Update(config =>
        {
            if (!config.Hosts.Any(host => host.Id == frozen.HostId)) throw new ArgumentException("Selected host no longer exists.");
            if (config.Monitoring.Monitors.Any(item => item.Id != frozen.Id && item.Name.Equals(frozen.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Monitor name already exists.");
            var existing = config.Monitoring.Monitors.FirstOrDefault(item => item.Id == frozen.Id);
            if (expected is not null && (existing is null || Serialized(existing) != Serialized(expected)))
                throw new InvalidOperationException("The monitor changed in another window. Reload before saving.");
            if (existing is not null && expected is null) throw new InvalidOperationException("Editing requires an expected monitor version.");
            if (existing is null && config.Monitoring.Monitors.Count >= 512) throw new ArgumentException("At most 512 monitors are supported.");
            config.Monitoring.Monitors.RemoveAll(item => item.Id == frozen.Id);
            config.Monitoring.Monitors.Add(frozen);
        });
        _failures.TryRemove(frozen.Id, out _);
        audit.Record(new() { Category = "config", EventType = "monitor.saved", HostId = frozen.HostId, Action = frozen.Name });
    }

    public async Task Delete(string id, CancellationToken cancellationToken = default)
    {
        await using var lease = await store.Lease(id, cancellationToken);
        repository.Update(config => config.Monitoring.Monitors.RemoveAll(item => item.Id == id));
        store.Delete(id);
        _failures.TryRemove(id, out _);
        audit.Record(new() { Category = "config", EventType = "monitor.deleted", Action = id });
    }

    private IEnumerable<MonitorDefinition> Select(IReadOnlyList<string>? hosts, string? kind) =>
        Definitions().Where(item => (hosts is null || hosts.Contains(item.HostId)) &&
            (kind is null || Providers.FirstOrDefault(provider => provider.Key == item.Provider)?.Kind == kind));

    public IReadOnlyList<MonitorSnapshot> Status(IReadOnlyList<string>? hostIds = null, string? kind = null)
    {
        var results = new List<MonitorSnapshot>();
        var hosts = repository.Load().Hosts;
        foreach (var definition in Select(hostIds, kind))
        {
            MonitorSnapshot current;
            try
            {
                var record = store.Load(definition.Id);
                current = record?.Snapshot ?? Empty(definition);
                if (_failures.TryGetValue(definition.Id, out var failed) && failed.ObservedUtc >= current.ObservedUtc)
                    current = failed;
                if (record is not null && !MonitoringScope.Matches(record, definition, hosts.FirstOrDefault(host => host.Id == definition.HostId)))
                {
                    current.Quality = MonitorDataQuality.Stale;
                    current.Severity = MonitorSeverity.Unknown;
                    current.Message = "The monitoring definition or endpoint changed. Run a new probe.";
                    foreach (var metric in current.Metrics) metric.Quality = MonitorDataQuality.Stale;
                }
            }
            catch (Exception error)
            {
                current = Empty(definition);
                current.Quality = MonitorDataQuality.Error;
                current.Message = AuditRedactor.Redact(error.Message);
            }
            if (!definition.Enabled) { current.Quality = MonitorDataQuality.Disabled; current.Severity = MonitorSeverity.Unknown; }
            else if (current.ObservedUtc != default && (clock.GetUtcNow() < current.ObservedUtc ||
                clock.GetUtcNow() - current.ObservedUtc > TimeSpan.FromSeconds(definition.FreshnessSeconds)))
                current.Quality = MonitorDataQuality.Stale;
            if (current.Quality is not MonitorDataQuality.Success && current.Severity == MonitorSeverity.Ok)
                current.Severity = MonitorSeverity.Unknown;
            results.Add(current);
        }
        return results;
    }

    public async Task<IReadOnlyList<MonitorSnapshot>> Run(IReadOnlyList<string>? hostIds = null, string? kind = null, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var config = repository.Load();
        var definitions = Select(hostIds, kind).Where(item => item.Enabled).ToList();
        var snapshots = new System.Collections.Concurrent.ConcurrentBag<MonitorSnapshot>();
        await Parallel.ForEachAsync(definitions.GroupBy(definition => definition.HostId), new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(config.Monitoring.MaxConcurrency, 1, 4), CancellationToken = linked.Token },
            async (group, token) => { foreach (var definition in group) { token.ThrowIfCancellationRequested(); snapshots.Add(await Collect(definition, token)); } });
        return snapshots.OrderBy(item => item.HostId).ThenBy(item => item.MonitorId).ToList();
    }

    private async Task<MonitorSnapshot> Collect(MonitorDefinition definition, CancellationToken cancellationToken, bool explicitRun = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var hostGate = _hosts.GetOrAdd(definition.HostId, _ => new SemaphoreSlim(1));
        await hostGate.WaitAsync(cancellationToken);
        try { await _parallel.WaitAsync(cancellationToken); }
        catch { hostGate.Release(); throw; }
        timeout.CancelAfter(TimeSpan.FromSeconds(definition.TimeoutSeconds));
        try
        {
            await using var lease = await store.Lease(definition.Id, timeout.Token);
            var previous = store.Load(definition.Id) ?? new();
            var config = repository.Load();
            var originalHostId = definition.HostId;
            definition = config.Monitoring.Monitors.FirstOrDefault(item => item.Id == definition.Id) ??
                throw new ArgumentException("The monitor no longer exists.");
            if (!definition.Enabled && !explicitRun)
            {
                var disabled = Empty(definition);
                disabled.Quality = MonitorDataQuality.Disabled;
                return disabled;
            }
            if (definition.HostId != originalHostId)
                return new()
                {
                    MonitorId = definition.Id,
                    HostId = definition.HostId,
                    Provider = definition.Provider,
                    Quality = MonitorDataQuality.Stale,
                    Message = "The monitor target changed while queued. Run a new probe."
                };
            var host = config.Hosts.FirstOrDefault(item => item.Id == definition.HostId);
            var scope = MonitoringScope.Fingerprint(definition, host);
            if ((previous.Snapshot.ObservedUtc != default || previous.State.Count > 0) && !MonitoringScope.Matches(previous, definition, host))
                previous = new() { Events = previous.Events.Append(new(clock.GetUtcNow(), "scope.changed", definition.Id, MonitorSeverity.Unknown)).ToList() };
            MonitoringObservation observation;
            var now = clock.GetUtcNow();
            try
            {
                if (host is null) throw new ArgumentException("The monitored host no longer exists.");
                MonitoringValidation.Validate(definition);
                var provider = Provider(definition);
                provider.Validate(definition);
                observation = await provider.Collect(host, definition, previous, timeout.Token);
                observation.Snapshot.LastSuccessUtc = observation.Snapshot.Quality == MonitorDataQuality.Success ? now : previous.Snapshot.LastSuccessUtc;
                if (observation.Snapshot.Quality is MonitorDataQuality.Success or MonitorDataQuality.Partial or MonitorDataQuality.Baseline)
                    observation.State.Remove(MonitoringStateKeys.Failures);
                else
                {
                    var failures = previous.State.TryGetValue(MonitoringStateKeys.Failures, out var oldCount) && int.TryParse(oldCount, out var oldValue) ? oldValue : 0;
                    observation.State[MonitoringStateKeys.Failures] = Math.Min(10, failures + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var quality = exception switch
                {
                    TimeoutException or OperationCanceledException => MonitorDataQuality.Timeout,
                    NotSupportedException => MonitorDataQuality.Unsupported,
                    UnauthorizedAccessException => MonitorDataQuality.PermissionDenied,
                    MonitoringProbeException probe => probe.Quality,
                    _ => MonitorDataQuality.Error
                };
                var count = previous.State.TryGetValue(MonitoringStateKeys.Failures, out var text) && int.TryParse(text, out var value) ? value : 0;
                observation = new()
                {
                    Snapshot = new()
                    {
                        Quality = quality,
                        LastSuccessUtc = previous.Snapshot.LastSuccessUtc,
                        Message = AuditRedactor.Redact(exception.Message),
                        Metrics = previous.Snapshot.Metrics
                    },
                    State = previous.State
                };
                foreach (var metric in observation.Snapshot.Metrics) metric.Quality = MonitorDataQuality.Stale;
                observation.State[MonitoringStateKeys.Failures] = Math.Min(10, count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var latest = repository.Load();
            var latestDefinition = latest.Monitoring.Monitors.FirstOrDefault(item => item.Id == definition.Id);
            if (latestDefinition is null || scope != MonitoringScope.Fingerprint(latestDefinition, latest.Hosts.FirstOrDefault(item => item.Id == latestDefinition.HostId)))
                return new()
                {
                    MonitorId = definition.Id,
                    HostId = definition.HostId,
                    Provider = definition.Provider,
                    ObservedUtc = now,
                    Quality = MonitorDataQuality.Stale,
                    Severity = MonitorSeverity.Unknown,
                    Message = "Configuration changed during the probe. Run a new probe."
                };
            observation.State[MonitoringStateKeys.Scope] = scope;
            observation.Snapshot.MonitorId = definition.Id;
            observation.Snapshot.HostId = definition.HostId;
            observation.Snapshot.Provider = definition.Provider;
            observation.Snapshot.ObservedUtc = now;
            foreach (var metric in observation.Snapshot.Metrics)
            {
                if (metric.Quality == MonitorDataQuality.Success) metric.SampledUtc = now;
                else
                {
                    var old = previous.Snapshot.Metrics.FirstOrDefault(item => item.Key == metric.Key && (item.NumericValue is not null || item.TextValue is not null));
                    if (old is not null && metric.NumericValue is null && metric.TextValue is null)
                    {
                        metric.NumericValue = old.NumericValue;
                        metric.TextValue = old.TextValue;
                        metric.SampledUtc = old.SampledUtc;
                        metric.Diagnostic = $"Previous sample retained; current data quality: {metric.Quality}.";
                        metric.Quality = MonitorDataQuality.Stale;
                    }
                    else if (metric.Quality == MonitorDataQuality.Partial && (metric.NumericValue is not null || metric.TextValue is not null))
                        metric.SampledUtc = now;
                }
            }
            var oldEvents = previous.Events.ToHashSet();
            var oldQuality = previous.Snapshot.Quality;
            evaluator.Evaluate(definition, observation, previous, now, Math.Clamp(repository.Load().Monitoring.MaxHistoryEntries, 1, 10000));
            previous.State = observation.State;
            previous.Snapshot = observation.Snapshot;
            if (Definitions().Any(item => item.Id == definition.Id)) store.Save(definition.Id, previous);
            _failures.TryRemove(definition.Id, out _);
            if (oldQuality != observation.Snapshot.Quality)
                audit.Record(new()
                {
                    Category = "monitoring",
                    EventType = "monitor.quality.changed",
                    HostId = definition.HostId,
                    Action = observation.Snapshot.Quality.ToString(),
                    Success = observation.Snapshot.Quality == MonitorDataQuality.Success
                });
            foreach (var change in previous.Events.Where(change => !oldEvents.Contains(change)))
                audit.Record(new()
                {
                    Category = "monitoring",
                    EventType = "monitor." + change.Action,
                    HostId = definition.HostId,
                    Action = change.AlarmKey,
                    Success = change.Action == "recovered"
                });
            return observation.Snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var snapshot = Empty(definition);
            snapshot.ObservedUtc = clock.GetUtcNow();
            snapshot.Quality = error is OperationCanceledException or TimeoutException ? MonitorDataQuality.Timeout : MonitorDataQuality.Error;
            snapshot.Message = AuditRedactor.Redact(error.Message);
            _failures[definition.Id] = snapshot;
            return snapshot;
        }
        finally { _parallel.Release(); hostGate.Release(); }
    }

    public async Task<MonitorSnapshot> RunOne(string monitorId, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await Collect(Find(monitorId), linked.Token, explicitRun: true);
    }

    public async Task Watch(Action<IReadOnlyList<MonitorSnapshot>> publish, IReadOnlyList<string>? hostIds = null,
        string? kind = null, CancellationToken cancellationToken = default)
    {
        if (!repository.Load().Monitoring.Enabled) throw new InvalidOperationException("Monitoring is disabled. Enable it explicitly before watch.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var due = new Dictionary<string, (long Timestamp, TimeSpan Delay, string Scope)>();
        while (repository.Load().Monitoring.Enabled)
        {
            linked.Token.ThrowIfCancellationRequested();
            var now = clock.GetTimestamp();
            var hosts = repository.Load().Hosts;
            var ready = Select(hostIds, kind).Where(item => item.Enabled && (!due.TryGetValue(item.Id, out var next) ||
                clock.GetElapsedTime(next.Timestamp, now) >= next.Delay ||
                next.Scope != MonitoringScope.Fingerprint(item, hosts.FirstOrDefault(host => host.Id == item.HostId)))).ToList();
            await Parallel.ForEachAsync(ready.GroupBy(definition => definition.HostId), new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(repository.Load().Monitoring.MaxConcurrency, 1, 4), CancellationToken = linked.Token },
                async (group, token) =>
                {
                    foreach (var definition in group) { token.ThrowIfCancellationRequested(); await Collect(definition, token); }
                });
            foreach (var definition in ready)
            {
                var record = store.Load(definition.Id);
                due[definition.Id] = (clock.GetTimestamp(), MonitoringSchedule.Delay(definition, record),
                    MonitoringScope.Fingerprint(definition, hosts.FirstOrDefault(host => host.Id == definition.HostId)));
            }
            publish(Status(hostIds, kind));
            await Task.Delay(TimeSpan.FromSeconds(1), clock, linked.Token);
        }
    }

    public async Task Acknowledge(string monitorId, string alarmKey, CancellationToken cancellationToken = default)
    {
        await using var lease = await store.Lease(monitorId, cancellationToken);
        var config = repository.Load();
        var definition = config.Monitoring.Monitors.FirstOrDefault(item => item.Id == monitorId)
            ?? throw new ArgumentException("Monitor no longer exists.");
        var record = store.Load(monitorId) ?? throw new ArgumentException("No monitoring result exists.");
        if (!MonitoringScope.Matches(record, definition, config.Hosts.FirstOrDefault(host => host.Id == definition.HostId)))
            throw new InvalidOperationException("The definition or endpoint changed. Run a new probe before acknowledging its alarms.");
        var alarm = record.Alarms.FirstOrDefault(item => item.Key == alarmKey && item.Active)
            ?? throw new ArgumentException("No active alarm matches.");
        alarm.Acknowledged = true;
        store.Save(monitorId, record);
        audit.Record(new() { Category = "monitoring", EventType = "monitor.acknowledged", HostId = record.Snapshot.HostId, Action = alarmKey });
    }

    private static MonitorSnapshot Empty(MonitorDefinition definition) => new() { MonitorId = definition.Id, HostId = definition.HostId, Provider = definition.Provider };
    private static string Serialized(MonitorDefinition definition) => JsonSerializer.Serialize(definition, MonitoringJsonContext.Default.MonitorDefinition);
    private static MonitorDefinition Clone(MonitorDefinition definition) => JsonSerializer.Deserialize(Serialized(definition), MonitoringJsonContext.Default.MonitorDefinition)!;
    public void Dispose() => _lifetime.Cancel();
}
