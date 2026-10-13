using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MacroPreparedProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualProcessServiceValidatesResolvedCredentialsBeforeStartingAndDisposesItsTunnel(bool nonInteractive)
    {
        var repository = new SecretProviderTestsFakeRepository();
        repository.Config.SecretProviders.Add(new() { Name = "prod", ProviderType = "Fake", IsDefault = true, CacheTtlSeconds = 0 });
        var factory = new SecretProviderTestsFakeFactory();
        factory.Provider.Value = "cobalt-value-123";
        var registry = new SecretProviderRegistry(repository, [factory], NullLogger<SecretProviderRegistry>.Instance);
        var bastion = new SecretProviderTestsCredentialBastion(BastionAuthType.Password);
        var runner = new MonitoringTransportTestDependencies();
        var audit = new MacroTestDependencies();
        var process = new ProcessService(NullLogger<ProcessService>.Instance, runner, bastion, registry, audit,
            applicationExecutable: Path.Combine(Path.GetTempPath(), "ssk"));
        var macros = new MacroService(repository, process, audit);
        var host = new ServerHost
        {
            Id = "host",
            Alias = "web",
            HostName = "vm",
            User = "operator",
            AuthType = AuthType.AzureBastion,
            PasswordSecretRef = new() { SecretId = "login", SecretType = SecretType.Password },
            AzureBastion = new()
            {
                AuthType = BastionAuthType.Aad,
                ConnectionMode = BastionConnectionMode.Native,
                BastionName = "bastion",
                BastionResourceGroup = "group",
                TargetIpAddress = "192.0.2.10"
            }
        };
        var macro = new MacroDefinition { Name = "check", Commands = ["printf '%s' 'cobalt-value-123'"] };
        var result = Assert.Single(nonInteractive ? await macros.RunNonInteractive(macro, [host], _ => { })
            : await macros.Run(macro, [host], _ => { }));
        Assert.False(result.Success);
        Assert.Null(runner.Info);
        Assert.Equal(1, factory.Provider.Accesses);
        Assert.True(bastion.Tunnel.Disposed);
        Assert.Null(host.SshPassword);
        Assert.Equal(BastionAuthType.Aad, host.AzureBastion.AuthType);
        Assert.DoesNotContain(audit.Events, entry => entry.Action?.Contains("cobalt-value-123", StringComparison.Ordinal) == true);
    }
}
