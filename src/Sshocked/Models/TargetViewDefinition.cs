namespace Sshocked.Models;

public sealed class TargetViewDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
}
