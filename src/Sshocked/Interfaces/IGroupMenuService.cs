namespace Sshocked.Interfaces;

public interface IGroupMenuService
{
    Task Browse();
    Task ShowGroup(string groupId) => Browse();
}
