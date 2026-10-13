namespace Sshocked.Models;

public sealed record MacroTemplate(string Name, string Description, IReadOnlyList<string> Commands);
