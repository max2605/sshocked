using Spectre.Console;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class PathInputServiceTests
{
    [Fact]
    public void QuotedPathIsNormalizedBeforeFileValidationAndReturnedWithoutQuotes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked path " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "identity file.pem");
        File.WriteAllText(path, "test");
        try
        {
            var result = Prompt(["\"" + path + "\""], validate: value => File.Exists(value) ? null : "Missing file");
            Assert.Equal(path, result);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void EmptyQuotedValueCancelsAndControlCharactersAreRejected()
    {
        Assert.Null(Prompt(["\"\""]));
        Assert.Equal("C:\\valid", Prompt(["bad\0path", "C:\\valid"]));
        Assert.Equal("C:\\valid", Prompt(["bad\npath", "C:\\valid"]));
    }

    private static string? Prompt(IEnumerable<string?> values, Func<string, string?>? validate = null)
    {
        var previous = AnsiConsole.Profile.Capabilities.Interactive;
        AnsiConsole.Profile.Capabilities.Interactive = false;
        try { return new PathInputService(new PathInputTestsConsole(values)).Prompt("Path", validate: validate); }
        finally { AnsiConsole.Profile.Capabilities.Interactive = previous; }
    }
}
