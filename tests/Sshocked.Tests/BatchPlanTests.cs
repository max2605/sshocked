using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class BatchPlanTests
{
    [Fact]
    public async Task BoundedWorkersKeepIndependentOutcomesAndDeduplicateTargets()
    {
        var fake = new FleetTestDependencies();
        var active = 0;
        var maximum = 0;
        var gate = new object();
        fake.Work = async (host, ct) =>
        {
            lock (gate) { active++; maximum = Math.Max(maximum, active); }
            try { await Task.Delay(15, ct); return host.Id == "2" ? 23 : 0; }
            finally { lock (gate) active--; }
        };
        var hosts = Enumerable.Range(0, 6).Select(index => Host(index.ToString())).ToList();
        var plan = new BatchPlanService().Create(BatchOperation.ForCommand("work"), hosts.Concat([hosts[0]]), 2);
        var report = await new BatchExecutorService(fake, fake, fake).Execute(plan);
        Assert.Equal(2, maximum);
        Assert.Equal(6, fake.Calls.Count);
        Assert.Equal(6, report.Results.Count);
        Assert.Equal(BatchTargetStatus.Failed, report.Results[2].Status);
        Assert.Equal(23, report.Results[2].ExitCode);
        Assert.Equal(5, report.Results.Count(result => result.Status == BatchTargetStatus.Success));
        Assert.All(report.Results, result => Assert.True(result.DurationSeconds >= 0));
    }

    [Fact]
    public async Task CancellationStopsDequeuingAndPreservesCompletedResults()
    {
        var fake = new FleetTestDependencies();
        using var cancellation = new CancellationTokenSource();
        fake.Work = (host, ct) =>
        {
            if (host.Id == "1") cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        };
        var plan = new BatchPlanService().Create(BatchOperation.ForCommand("work"), [Host("0"), Host("1"), Host("2"), Host("3")], 1);
        var report = await new BatchExecutorService(fake, fake, fake).Execute(plan, cancellationToken: cancellation.Token);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal([BatchTargetStatus.Success, BatchTargetStatus.Cancelled, BatchTargetStatus.NotStarted, BatchTargetStatus.NotStarted], report.Results.Select(result => result.Status));
        Assert.False(report.Success);
    }

    [Fact]
    public async Task PlanAndRetryKeepOriginalHostAndMacroSnapshots()
    {
        var fake = new FleetTestDependencies();
        var host = Host("a");
        var macro = new MacroDefinition { Name = "job", Commands = ["echo original"] };
        var plans = new BatchPlanService();
        var plan = plans.Create(BatchOperation.ForMacro(macro), [host]);
        host.HostName = "changed";
        macro.Commands[0] = "echo changed";
        fake.Work = (_, _) => Task.FromResult(9);
        var executor = new BatchExecutorService(fake, fake, fake);
        var report = await executor.Execute(plan);
        var retry = plans.RetryFailed(plan, report, ["a"]);
        await executor.Execute(retry);
        Assert.All(fake.Calls, call => { Assert.Equal("original", call.HostName); Assert.Equal("echo original", call.Payload); });
        Assert.Equal(plan.Id, retry.RetryOf);
        Assert.Throws<ArgumentException>(() => plans.RetryFailed(plan, report, []));
        Assert.Throws<ArgumentException>(() => plans.RetryFailed(plan, report, ["missing"]));
        Assert.Throws<ArgumentException>(() => plans.RetryFailed(plan, report with { PlanId = "other" }, ["a"]));
    }

    [Fact]
    public async Task TransfersUseNoninteractiveCallsAndUniqueDownloadDestinations()
    {
        var fake = new FleetTestDependencies();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var local = Path.Combine(directory, "source.txt");
            File.WriteAllText(local, "source");
            var hosts = new[] { Host("a"), Host("b") };
            var plans = new BatchPlanService();
            var executor = new BatchExecutorService(fake, fake, fake);
            Assert.True((await executor.Execute(plans.Create(BatchOperation.ForUpload(local, "/tmp/source"), hosts, 2))).Success);
            Assert.True((await executor.Execute(plans.Create(BatchOperation.ForDownload("/tmp/source", directory), hosts, 2))).Success);
            Assert.Equal(2, fake.Calls.Count(call => call.Operation == "upload"));
            var downloaded = fake.Calls.Where(call => call.Operation == "download").Select(call => call.Payload).ToList();
            Assert.Equal(2, downloaded.Distinct().Count());
            Assert.All(downloaded, path => Assert.StartsWith(directory, path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReportsContainOutcomesWithoutCapturedOutputOrCommandSecrets()
    {
        var fake = new FleetTestDependencies { Work = (_, _) => throw new IOException("password=secret /private/path") };
        var plan = new BatchPlanService().Create(BatchOperation.ForCommand("SECRET_COMMAND"), [Host("a")]);
        var report = await new BatchExecutorService(fake, fake, fake).Execute(plan);
        var json = BatchReportFormatter.Format(report, "json");
        Assert.DoesNotContain("SECRET_COMMAND", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("/private/path", json);
        Assert.Equal("IOException", report.Results[0].Error);
        var csv = BatchReportFormatter.Format(report with { Results = [report.Results[0] with { Alias = "=HYPERLINK(\"unsafe\")" }] }, "csv");
        Assert.Contains("'=", csv);
    }

    private static ServerHost Host(string id) => new() { Id = id, Alias = "host-" + id, HostName = "original", User = "tester" };
}
