namespace Sshocked.Interfaces;

public interface IUiChromeService
{
    void RenderBreadcrumb(params string[] segments);
    void RenderFooter();
}
