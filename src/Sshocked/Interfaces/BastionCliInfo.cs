namespace Sshocked.Interfaces;

public sealed record BastionCliInfo(bool IsInstalled, Version? CliVersion, bool HasBastionExtension, bool HasSshExtension, bool IsLoggedIn, string? ErrorMessage);
