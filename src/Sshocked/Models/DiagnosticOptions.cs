namespace Sshocked.Models;

public sealed record DiagnosticOptions(bool IncludePaths = false, bool IncludeCommandText = false, bool Probe = false);
