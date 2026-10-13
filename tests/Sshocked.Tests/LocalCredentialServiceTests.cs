using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class LocalCredentialServiceTests
{
    [Fact]
    public void PasswordIsEncryptedBoundToEndpointAndCanBeRotatedOrCleared()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-dpapi-test-" + Guid.NewGuid().ToString("N"));
        var service = new LocalCredentialService(directory);
        var host = new ServerHost { Id = "../../external", HostName = "original", User = "alice", AuthType = AuthType.Password };
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.False(service.IsSupported);
                Assert.Throws<PlatformNotSupportedException>(() => service.SavePassword(host, "private-value"));
                Assert.False(Directory.Exists(directory));
                return;
            }
            service.SavePassword(host, "private-value");
            Assert.True(service.HasPassword(host));
            Assert.Equal("private-value", service.GetPassword(host));
            var file = Assert.Single(Directory.GetFiles(directory, "*.dpapi"));
            Assert.Equal(directory, Path.GetDirectoryName(file));
            Assert.DoesNotContain("private-value", File.ReadAllText(file));
            var access = new DirectoryInfo(directory).GetAccessControl();
            Assert.True(access.AreAccessRulesProtected);
            foreach (FileSystemAccessRule rule in access.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow) Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference);
            host.HostName = "different-server";
            Assert.Throws<InvalidOperationException>(() => service.GetPassword(host));
            service.SavePassword(host, "rotated");
            Assert.Equal("rotated", service.GetPassword(host));
            File.WriteAllBytes(file, [1, 2, 3]);
            Assert.Throws<InvalidOperationException>(() => service.GetPassword(host));
            service.ClearPassword(host);
            Assert.False(service.HasPassword(host));
            Assert.Null(service.GetPassword(host));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void BastionRoutingChangesInvalidateTheSavedPassword()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-dpapi-route-test-" + Guid.NewGuid().ToString("N"));
        var service = new LocalCredentialService(directory);
        var host = new ServerHost
        {
            Id = "bastion",
            HostName = "private",
            User = "alice",
            AzureBastion = new()
            { BastionName = "same-name", BastionResourceGroup = "same-group", TargetIpAddress = "10.0.0.5", SubscriptionId = "original", TenantId = "tenant", TargetPort = 22 }
        };
        try
        {
            service.SavePassword(host, "private-value");
            host.AzureBastion.SubscriptionId = "other-subscription";
            Assert.Throws<InvalidOperationException>(() => service.GetPassword(host));
            host.AzureBastion.SubscriptionId = "original";
            host.AzureBastion.TenantId = "other-tenant";
            Assert.Throws<InvalidOperationException>(() => service.GetPassword(host));
            host.AzureBastion.TenantId = "tenant";
            host.AzureBastion.TargetPort = 2222;
            Assert.Throws<InvalidOperationException>(() => service.GetPassword(host));
            host.AzureBastion.TargetPort = 22;
            Assert.Equal("private-value", service.GetPassword(host));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SavedPasswordFeedsAskpassWithoutChangingHostOrProcessArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-local-login-test-" + Guid.NewGuid().ToString("N"));
        var service = new LocalCredentialService(directory);
        var host = new ServerHost { Id = "server", HostName = "target", User = "alice", AuthType = AuthType.Password };
        try
        {
            service.SavePassword(host, "saved-password");
            var runner = new SecretProviderTestsInspectRunner(info =>
            {
                Assert.Equal("saved-password", info.Environment[SshAskPass.PasswordEnvironment]);
                Assert.DoesNotContain("saved-password", string.Join(' ', info.ArgumentList));
                return new ProcessResult(0, "connected", "");
            });
            var registry = new SecretProviderRegistry(new SecretProviderTestsFakeRepository(), [], NullLogger<SecretProviderRegistry>.Instance);
            var audit = new ConnectionHookTestsAudit();
            var process = new ProcessService(NullLogger<ProcessService>.Instance, runner,
                new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), registry,
                audit: audit, applicationExecutable: "ssk.exe", localCredentials: service);
            Assert.Equal("connected", await process.RunSshCommand(host, "hostname"));
            Assert.Null(host.SshPassword);
            Assert.Null(host.PasswordSecretRef);
            var literalRunner = new SecretProviderTestsInspectRunner(_ => new ProcessResult(0, "", ""));
            var literalProcess = new ProcessService(NullLogger<ProcessService>.Instance, literalRunner,
                new AzureBastionService(literalRunner, NullLogger<AzureBastionService>.Instance), registry,
                audit: audit, applicationExecutable: "ssk.exe", localCredentials: service);
            await literalProcess.RunSshCommand(host, "printf '%s' 'saved-password'");
            Assert.DoesNotContain("saved-password", audit.Entries.Last().Action);
            Assert.Contains("[REDACTED]", audit.Entries.Last().Action);
            service.ClearAll();
            Assert.False(service.HasPassword(host));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
