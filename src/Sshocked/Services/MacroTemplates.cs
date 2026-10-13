using Sshocked.Models;

namespace Sshocked.Services;

public static class MacroTemplates
{
    public static IReadOnlyList<MacroTemplate> All { get; } = Array.AsReadOnly(new[]
    {
        new MacroTemplate("Ubuntu/Debian: update package index and upgrade packages",
            "Ubuntu/Debian only. Requires noninteractive sudo permission. Package scripts may still need input; use Connect for interactive work.",
            Array.AsReadOnly(new[] { "sudo -n apt update", "sudo -n apt upgrade -y" }))
    });
}
