using Microsoft.Extensions.Logging;
using Sshocked.Logging;

namespace Sshocked.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public void ShutdownDrainsQueuedMessagesAndRedactsDiagnosticSecrets()
    {
        var path = Path.Combine(Path.GetTempPath(), "ssk-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var processor = new FileLoggerProcessor(path))
            {
                var logger = new FileLogger("test", processor);
                for (var i = 0; i < 100; i++) logger.LogInformation("Message {Index}", i);
                logger.LogError(new InvalidOperationException("tool --token SECRET_VALUE"), "tool --password SECRET_VALUE");
            }
            var text = File.ReadAllText(Path.Combine(path, "sshocked.log"));
            Assert.Contains("Message 99", text);
            Assert.DoesNotContain("SECRET_VALUE", text);
            Assert.Contains("[REDACTED]", text);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
