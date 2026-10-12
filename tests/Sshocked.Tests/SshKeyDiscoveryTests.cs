using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class SshKeyDiscoveryTests
{
    private readonly SshKeyDiscoveryService _service = new(NullLogger<SshKeyDiscoveryService>.Instance);

    [Fact]
    public void PublicKeyMetadataUsesOpenSshSha256Fingerprint()
    {
        var blob = Blob("ssh-ed25519");
        var publicKey = "ssh-ed25519 " + Convert.ToBase64String(blob) + " a workstation key";
        var key = _service.ParsePublicKey(publicKey, "/keys/private");
        Assert.NotNull(key);
        Assert.Equal("ED25519", key.Algorithm);
        Assert.Equal("a workstation key", key.Comment);
        Assert.Equal("SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('='), key.Fingerprint);
        Assert.Equal("/keys/private", key.Path);
    }

    [Theory]
    [InlineData("not a public key")]
    [InlineData("ssh-ed25519 invalid!")]
    [InlineData("ssh-ed25519 YQ==")]
    public void InvalidPublicKeysAreSkipped(string value)
        => Assert.Null(_service.ParsePublicKey(value));

    [Fact]
    public void DiscoveryFindsStandardAndConfigReferencedPairsAndSkipsPrivateOnlyFiles()
    {
        var home = Path.Combine(Path.GetTempPath(), "sshocked-keys-" + Guid.NewGuid().ToString("N"));
        var sshDirectory = Path.Combine(home, ".ssh");
        Directory.CreateDirectory(sshDirectory);
        try
        {
            var standard = Path.Combine(sshDirectory, "id_ed25519");
            var custom = Path.Combine(home, "custom key");
            File.WriteAllText(standard, "private contents must never be parsed");
            File.WriteAllText(standard + ".pub", "ssh-ed25519 " + Convert.ToBase64String(Blob("ssh-ed25519")));
            File.WriteAllText(custom, "private contents must never be parsed");
            File.WriteAllText(custom + ".pub", "ssh-rsa " + Convert.ToBase64String(Blob("ssh-rsa")) + " custom");
            File.WriteAllText(Path.Combine(sshDirectory, "id_missing_pub"), "private");
            File.WriteAllText(Path.Combine(sshDirectory, "config"), "Host sample\n IdentityFile \"~/custom key\" # metadata\n");
            var keys = _service.DiscoverKeys(sshDirectory);
            Assert.Equal(2, keys.Count);
            Assert.Contains(keys, key => key.Path == standard);
            Assert.Contains(keys, key => key.Path == custom && key.Algorithm == "RSA" && key.Comment == "custom");
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    private static byte[] Blob(string algorithm)
    {
        var name = Encoding.ASCII.GetBytes(algorithm);
        var result = new byte[4 + name.Length + 36];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)name.Length);
        name.CopyTo(result.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4 + name.Length), 32);
        return result;
    }
}
