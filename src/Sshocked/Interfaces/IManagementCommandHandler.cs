namespace Sshocked.Interfaces;

public interface IManagementCommandHandler
{
    bool Handles(string command);
    Task Execute(string command, string[] arguments);
}
