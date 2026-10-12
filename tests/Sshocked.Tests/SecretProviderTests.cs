using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SecretProviderTests
{
    [Fact]
    public async Task RegistryUsesDefaultProviderAndCachesValuesOnlyInMemory()
    {
        var repository = new SecretProviderTestsFakeRepository();
        repository.Config.SecretProviders.Add(new() { Name = "prod", ProviderType = "Fake", IsDefault = true, CacheTtlSeconds = 60 });
        var factory = new SecretProviderTestsFakeFactory();
        var registry = new SecretProviderRegistry(repository, [factory], NullLogger<SecretProviderRegistry>.Instance);
        var reference = new SecretRef { SecretId = "key", SecretType = SecretType.SshPrivateKey };
        Assert.True((await registry.Resolve(reference)).Success);
        Assert.True((await registry.Resolve(reference)).Success);
        Assert.Equal(1, factory.Provider.Accesses);
        Assert.Equal(0, repository.Saves);
        registry.ClearCache();
        await registry.Resolve(reference);
        Assert.Equal(2, factory.Provider.Accesses);
        Assert.DoesNotContain("super-secret", new SecretResult { Success = true, Value = "super-secret" }.ToString());
    }

    [Fact]
    public async Task DisablingCacheImmediatelyUsesRotatedValue()
    {
        var repository = new SecretProviderTestsFakeRepository();
        var config = new SecretProviderConfig { Name = "prod", ProviderType = "Fake", IsDefault = true, CacheTtlSeconds = 60 };
        repository.Config.SecretProviders.Add(config);
        var factory = new SecretProviderTestsFakeFactory();
        var registry = new SecretProviderRegistry(repository, [factory], NullLogger<SecretProviderRegistry>.Instance);
        var reference = new SecretRef { SecretId = "password", SecretType = SecretType.Password };
        await registry.Resolve(reference);
        config.CacheTtlSeconds = 0;
        factory.Provider.Value = "rotated";
        Assert.Equal("rotated", (await registry.Resolve(reference)).Value);
        Assert.Equal(2, factory.Provider.Accesses);
    }

    [Fact]
    public void MultipleDefaultsOrMissingNamedProviderAreRejected()
    {
        var repository = new SecretProviderTestsFakeRepository();
        repository.Config.SecretProviders.AddRange([new() { Name = "one", IsDefault = true }, new() { Name = "two", IsDefault = true }]);
        var registry = new SecretProviderRegistry(repository, [new SecretProviderTestsFakeFactory()], NullLogger<SecretProviderRegistry>.Instance);
        Assert.Throws<InvalidOperationException>(() => registry.GetProvider());
        Assert.Throws<InvalidOperationException>(() => registry.GetProvider("missing"));
    }

    [Fact]
    public void SecretUriMustMatchConfiguredVault()
    {
        var provider = new AzureKeyVaultSecretProvider(new SecretProviderConfig { Name = "prod", VaultUri = "https://prod.vault.azure.net/" });
        Assert.Equal(("key", "version"), provider.ParseSecretIdentifier("https://prod.vault.azure.net/secrets/key/version"));
        Assert.Throws<ArgumentException>(() => provider.ParseSecretIdentifier("https://other.vault.azure.net/secrets/key"));
        Assert.Throws<ArgumentException>(() => provider.ParseSecretIdentifier("bad/name"));
        Assert.Throws<ArgumentException>(() => provider.ParseSecretIdentifier("https://prod.vault.azure.net/keys/key"));
    }

    [Fact]
    public async Task TemporaryPrivateKeyIsReadableAndDeletedOnDispose()
    {
        var file = await SecretKeyFile.Create("-----BEGIN OPENSSH PRIVATE KEY-----\nexample\n-----END OPENSSH PRIVATE KEY-----", SecretType.SshPrivateKey);
        var path = file.Path;
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", await File.ReadAllTextAsync(path));
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User;
            var directorySecurity = new DirectoryInfo(Path.GetDirectoryName(path)!).GetAccessControl();
            Assert.True(directorySecurity.AreAccessRulesProtected);
            foreach (FileSystemAccessRule rule in new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow) Assert.Equal(user, rule.IdentityReference);
        }
        else Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        await file.DisposeAsync();
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task ExportableCertificateSecretProducesTemporaryPemPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=sshocked-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var exported = certificate.Export(X509ContentType.Pkcs12);
        try
        {
            await using var file = await SecretKeyFile.Create(Convert.ToBase64String(exported), SecretType.Certificate);
            Assert.StartsWith("-----BEGIN PRIVATE KEY-----", await File.ReadAllTextAsync(file.Path));
        }
        finally { CryptographicOperations.ZeroMemory(exported); }
    }

    [Fact]
    public async Task ConnectionUsesTemporaryKeyAndCleansItAfterFailedSshWithoutMutatingHost()
    {
        var repository = new SecretProviderTestsFakeRepository();
        repository.Config.SecretProviders.Add(new() { Name = "prod", ProviderType = "Fake", IsDefault = true });
        var factory = new SecretProviderTestsFakeFactory();
        factory.Provider.Value = "-----BEGIN OPENSSH PRIVATE KEY-----\nexample\n-----END OPENSSH PRIVATE KEY-----";
        var registry = new SecretProviderRegistry(repository, [factory], NullLogger<SecretProviderRegistry>.Instance);
        string? keyPath = null;
        var runner = new SecretProviderTestsInspectRunner(info =>
        {
            keyPath = info.ArgumentList[info.ArgumentList.IndexOf("-i") + 1];
            Assert.True(File.Exists(keyPath));
            Assert.DoesNotContain("PRIVATE KEY", string.Join(' ', info.ArgumentList));
            return new ProcessResult(23, "", "failed");
        });
        var process = new ProcessService(NullLogger<ProcessService>.Instance, runner,
            new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), registry);
        var host = new ServerHost { User = "user", HostName = "vm", IdentityFile = "original", KeySecretRef = new() { SecretId = "key", SecretType = SecretType.SshPrivateKey } };
        Assert.Equal(23, await process.RunSshSession(host));
        Assert.False(File.Exists(keyPath));
        Assert.Equal("original", host.IdentityFile);
    }

    [Fact]
    public async Task RemoteCommandFailureHasSeparateStdoutStderrAndLegacyWrapperThrows()
    {
        var repository = new SecretProviderTestsFakeRepository();
        var registry = new SecretProviderRegistry(repository, [new SecretProviderTestsFakeFactory()], NullLogger<SecretProviderRegistry>.Instance);
        var runner = new SecretProviderTestsInspectRunner(_ => new ProcessResult(42, "partial-output", "permission denied"));
        var process = new ProcessService(NullLogger<ProcessService>.Instance, runner,
            new AzureBastionService(runner, NullLogger<AzureBastionService>.Instance), registry);
        var host = new ServerHost { User = "user", HostName = "vm" };
        var result = await process.RunSshCommandResult(host, "do something");
        Assert.Equal("partial-output", result.StandardOutput);
        Assert.Equal("permission denied", result.StandardError);
        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(() => process.RunSshCommand(host, "do something"));
        Assert.Equal(42, exception.ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VaultCredentialsUseCheckedTunnelCommandsEvenWhenStoredBastionAuthIsAad(bool password)
    {
        var repository = new SecretProviderTestsFakeRepository();
        repository.Config.SecretProviders.Add(new() { Name = "prod", ProviderType = "Fake", IsDefault = true });
        var factory = new SecretProviderTestsFakeFactory();
        factory.Provider.Value = password ? "private-login-value" : "-----BEGIN OPENSSH PRIVATE KEY-----\nexample\n-----END OPENSSH PRIVATE KEY-----";
        var registry = new SecretProviderRegistry(repository, [factory], NullLogger<SecretProviderRegistry>.Instance);
        var bastion = new SecretProviderTestsCredentialBastion(password ? BastionAuthType.Password : BastionAuthType.SshKey);
        var runner = new SecretProviderTestsInspectRunner(info =>
        {
            Assert.Equal("ssh", info.FileName);
            Assert.Contains("user@127.0.0.1", info.ArgumentList);
            Assert.Contains("42522", info.ArgumentList);
            Assert.Contains("HostKeyAlias=bastion:/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm", info.ArgumentList);
            Assert.DoesNotContain(factory.Provider.Value, string.Join(' ', info.ArgumentList));
            if (password) Assert.Equal(factory.Provider.Value, info.Environment["SSHOCKED_ASKPASS_PASSWORD"]);
            else Assert.True(File.Exists(info.ArgumentList[info.ArgumentList.IndexOf("-i") + 1]));
            return new ProcessResult(37, "partial", "failed");
        });
        var process = new ProcessService(NullLogger<ProcessService>.Instance, runner, bastion, registry);
        var host = new ServerHost
        {
            User = "user",
            HostName = "vm",
            AuthType = AuthType.AzureBastion,
            AzureBastion = new()
            {
                AuthType = BastionAuthType.Aad,
                ConnectionMode = BastionConnectionMode.Native,
                BastionName = "bastion",
                BastionResourceGroup = "rg",
                TargetResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm"
            }
        };
        var reference = new SecretRef { SecretId = "credential", SecretType = password ? SecretType.Password : SecretType.SshPrivateKey };
        if (password) host.PasswordSecretRef = reference; else host.KeySecretRef = reference;
        var result = await process.RunSshCommandResult(host, "exit 37");
        Assert.Equal(37, result.ExitCode);
        Assert.True(bastion.Tunnel.Disposed);
        if (!password) Assert.False(File.Exists(bastion.KeyPath));
        Assert.Equal(BastionAuthType.Aad, host.AzureBastion.AuthType);
        Assert.Null(host.IdentityFile);
    }
}
