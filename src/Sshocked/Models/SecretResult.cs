namespace Sshocked.Models;
// Deliberately not a record: generated ToString must never reveal a secret value.
public sealed class SecretResult
{
    public bool Success { get; init; }
    public string? Value { get; init; }
    public string? TempFilePath { get; init; }
    public string? ErrorMessage { get; init; }
}
