namespace Sshocked.Interfaces;

public interface ITagMenuService
{
    Task Show();
    Task ShowTag(string tag) => Show();
}
