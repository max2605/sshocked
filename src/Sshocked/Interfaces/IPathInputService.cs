namespace Sshocked.Interfaces;

public interface IPathInputService
{
    string? Prompt(string label, string? defaultValue = null, bool allowEmpty = true,
        Func<string, string?>? validate = null);
}
