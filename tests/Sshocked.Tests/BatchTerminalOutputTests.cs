using System.Text.Json;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class BatchTerminalOutputTests
{
    [Fact]
    public void TerminalOutputHasHostPrefixesAndCannotMoveTheCursorOrSetClipboard()
    {
        var fake = new FleetTestDependencies();
        var host = new BatchTarget(new() { Id = "host", Alias = "prod\x1b[31m" });
        BatchTerminalOutput.Write(fake, host, "\x1b[2Jfirst\x1b[0m\r\n\x1b]52;c;clipboard-secret\x07second\b\tvalue\n");
        Assert.Equal(["[prod] first", "[prod] second\tvalue"], fake.Output);
        Assert.DoesNotContain(fake.Output, line => line.Contains('\x1b') || line.Contains("clipboard-secret"));
    }

    [Fact]
    public async Task ConcurrentTargetsSerializeOutputCallbacksAndReportsExcludeRawContent()
    {
        var fake = new FleetTestDependencies { Work = async (_, ct) => { await Task.Delay(20, ct); return 0; } };
        var hosts = Enumerable.Range(0, 8).Select(index => new ServerHost { Id = index.ToString(), Alias = "host" + index }).ToList();
        var plan = new BatchPlanService().Create(BatchOperation.ForCommand("echo output"), hosts, 4);
        var active = 0;
        var maximum = 0;
        var writes = new List<string>();
        var report = await new BatchExecutorService(fake, fake, fake).Execute(plan, output: (target, text) =>
        {
            var count = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, count);
            Thread.Sleep(10);
            writes.Add(target.HostId + ":" + text);
            Interlocked.Decrement(ref active);
        });
        Assert.Equal(1, maximum);
        Assert.Equal(16, writes.Count);
        Assert.All(hosts, host => Assert.Equal(2, writes.Count(line => line.StartsWith(host.Id + ":", StringComparison.Ordinal))));
        Assert.DoesNotContain(fake.RemoteOutput, BatchReportFormatter.Format(report, "json"));
        Assert.DoesNotContain(fake.RemoteError, BatchReportFormatter.Format(report, "csv"));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    public async Task MachineReadableCliOutputExcludesPreviewAndCommandOutput(string format)
    {
        var fake = new FleetTestDependencies();
        fake.Config.Hosts.Add(new() { Id = "host", Alias = "production" });
        var views = new TargetViewService(fake, fake);
        var commands = new BatchCommandService(new BatchPlanService(), new BatchExecutorService(fake, fake, fake), new(fake, views), fake, fake);
        await commands.Execute("batch", ["command", "uptime", "--server", "host", "--execute", "--output", format]);
        var output = string.Join("\n", fake.Output);
        Assert.DoesNotContain(fake.RemoteOutput, output);
        Assert.DoesNotContain(fake.RemoteError, output);
        Assert.DoesNotContain("Batch preview", output);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        }
        else Assert.StartsWith("hostId,alias,status", output);
    }

    [Fact]
    public async Task TableCliShowsSafeCommandOutputAndMacroProgressSeparately()
    {
        var fake = new FleetTestDependencies { RemoteOutput = "\x1b[31muptime result\x1b[0m\n", RemoteError = "warning\n" };
        fake.Config.Hosts.Add(new() { Id = "host", Alias = "production" });
        fake.Config.Macros.Add(new() { Name = "check", Commands = ["uptime"] });
        var commands = new BatchCommandService(new BatchPlanService(), new BatchExecutorService(fake, fake, fake),
            new(fake, new TargetViewService(fake, fake)), fake, fake);
        await commands.Execute("batch", ["command", "uptime", "--server", "host", "--execute"]);
        Assert.Contains("[production] uptime result", fake.Output);
        Assert.Contains("[production] warning", fake.Output);
        Assert.DoesNotContain(fake.Output, line => line.Contains('\x1b'));
        fake.Output.Clear();
        await commands.Execute("batch", ["macro", "check", "--server", "host", "--execute"]);
        Assert.Contains("[production] uptime result", fake.Output);
    }
}
