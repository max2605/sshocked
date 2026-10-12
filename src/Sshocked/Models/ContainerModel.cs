namespace Sshocked.Models;

public sealed class ContainerModel
{
    public string? Id { get; set; }
    public string? Alias { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Ports { get; set; }
    public string? ComposeProject { get; set; }
    public string GroupId { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool IsFavorite { get; set; }
}
