namespace Sshocked.Interfaces;

public interface IUiChromeService
{
    void ApplyTheme();
    void RenderBreadcrumb(params string[] segments);
    void RenderFooter();
}
