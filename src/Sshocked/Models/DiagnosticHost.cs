namespace Sshocked.Models;

public sealed record DiagnosticHost(string Id, string Alias, string Hostname, int Port, string User,
    string Authentication, string Route, string? IdentityPath, bool HasKeyReference, bool HasPasswordReference,
    bool HasSudoReference, string? ProxyJump);
