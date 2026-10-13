namespace Sshocked.Models;

public sealed class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Commands { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? RecordedOnHostId { get; set; }

    public MacroDefinition Copy() => new()
    {
        Id = Id,
        Name = Name,
        Commands = Commands.ToList(),
        CreatedAt = CreatedAt,
        RecordedOnHostId = RecordedOnHostId
    };

    public bool Matches(MacroDefinition other) => Id == other.Id && Name == other.Name &&
        CreatedAt == other.CreatedAt && RecordedOnHostId == other.RecordedOnHostId &&
        Commands.SequenceEqual(other.Commands, StringComparer.Ordinal);
}
