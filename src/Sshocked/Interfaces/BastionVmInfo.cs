namespace Sshocked.Interfaces;

public sealed record BastionVmInfo(string Name, string ResourceId, string? PrivateIp, string? OsType);
