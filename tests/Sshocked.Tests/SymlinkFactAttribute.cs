namespace Sshocked.Tests;
/// <summary>Unix CI can create file links without elevation; Windows requires an explicit privilege.</summary>
public sealed class SymlinkFactAttribute : FactAttribute
{
    public SymlinkFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Requires administrator or Developer Mode on Windows; exercised by Unix CI.";
    }
}
