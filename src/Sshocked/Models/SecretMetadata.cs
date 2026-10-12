namespace Sshocked.Models;

public sealed record SecretMetadata(string Name, string Id, SecretType? SecretType, string? ContentType);
