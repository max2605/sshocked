using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class SecretProviderRegistry(IConfigRepository configRepository, IEnumerable<ISecretProviderFactory> factories, ILogger<SecretProviderRegistry> logger, IAuditService? audit = null) : ISecretProviderRegistry
{
    private readonly ConcurrentDictionary<string, ISecretProvider> providers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, SecretResult Result)> cache = new(StringComparer.Ordinal);

    public IReadOnlyList<SecretProviderConfig> GetConfigurations() => configRepository.Load().SecretProviders;

    public ISecretProvider GetProvider(string? name = null)
    {
        var config = FindConfiguration(name);
        var factory = factories.SingleOrDefault(f => f.ProviderType.Equals(config.ProviderType, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Secret provider type '{config.ProviderType}' is not registered.");
        return providers.GetOrAdd(ConfigurationKey(config), _ => factory.Create(config));
    }

    public async Task<SecretResult> ResolveAsync(SecretRef reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(reference.SecretId)) throw new ArgumentException("Secret identifier is required.");
        var config = FindConfiguration(reference.ProviderName);
        var key = ConfigurationKey(config) + "\n" + reference.SecretType + "\n" + reference.SecretId;
        if (config.CacheTtlSeconds > 0 && cache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Secret accessed from memory cache: provider {Provider}, identifier {SecretId}, user {User}", config.Name, reference.SecretId, Environment.UserName);
            AuditAccess(config.Name, reference.SecretId, true);
            return entry.Result;
        }
        // Expired entries are removed before retrieval so values do not survive failed rotations.
        cache.TryRemove(key, out _);
        var result = await GetProvider(config.Name).GetSecretAsync(reference.SecretId, reference.SecretType, ct);
        logger.LogInformation("Secret access: provider {Provider}, identifier {SecretId}, user {User}, success {Success}", config.Name, reference.SecretId, Environment.UserName, result.Success);
        AuditAccess(config.Name, reference.SecretId, result.Success);
        if (result.Success && config.CacheTtlSeconds > 0)
            cache[key] = (DateTimeOffset.UtcNow.AddSeconds(Math.Min(config.CacheTtlSeconds, 3600)), result);
        foreach (var stale in cache.Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow)) cache.TryRemove(stale.Key, out _);
        return result;
    }

    public void ClearCache() { cache.Clear(); providers.Clear(); }

    private void AuditAccess(string provider, string secretId, bool success) => audit?.Record(new AuditEntry
    {
        EventType = "secret.accessed", Category = "secret", Action = provider + ":" + secretId, Success = success
    });

    private SecretProviderConfig FindConfiguration(string? name)
    {
        var configurations = GetConfigurations();
        var matches = configurations.Where(c => name == null ? c.IsDefault : string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (name == null && matches.Count == 0 && configurations.Count == 1) return configurations[0];
        if (matches.Count != 1) throw new InvalidOperationException(name == null ? "Configure exactly one default secret provider." : $"Configure exactly one secret provider named '{name}'.");
        return matches[0];
    }

    private static string ConfigurationKey(SecretProviderConfig config) => string.Join('\n', config.Name, config.ProviderType, config.VaultUri, config.TenantId, config.ClientId);
}
