namespace Sshocked.Models;

public sealed record DiagnosticTool(string Name, bool Available, string? Version, string? Path, string? Note = null);
