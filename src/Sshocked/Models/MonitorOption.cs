namespace Sshocked.Models;

public sealed record MonitorOption(string Key, string Prompt, string DefaultValue = "", bool Required = false, string[]? Choices = null);
