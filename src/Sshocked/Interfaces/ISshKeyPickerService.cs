namespace Sshocked.Interfaces;

public interface ISshKeyPickerService
{
    string? Pick(string? currentPath = null);
    void ShowKeys();
}
