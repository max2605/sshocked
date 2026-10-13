using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MacroServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-macros-" + Guid.NewGuid().ToString("N"));
    private readonly MacroTestDependencies dependencies = new();
    private readonly ServerHost host = new() { Id = "one", Alias = "web", HostName = "localhost", User = "user" };
    private JsonConfigRepository Config() => new(Path.Combine(directory, "appconfig.json"));

    [Fact]
    public void RecordingRequiresStopAndPersistsOnlyExplicitCommands()
    {
        var config = Config();
        var service = new MacroService(config, dependencies, dependencies);
        var recording = new MacroRecording(host);
        recording.Capture("not recorded"); recording.Start(); recording.Capture("cd /tmp"); recording.Capture("printf 'hello'");
        Assert.Throws<InvalidOperationException>(() => service.Save(recording, "deploy"));
        recording.Stop(); recording.Capture("not recorded either");
        var saved = service.Save(recording, "deploy");
        Assert.Equal(new[] { "cd /tmp", "printf 'hello'" }, service.Find("DEPLOY").Commands);
        Assert.Equal(host.Id, saved.RecordedOnHostId);
        Assert.Throws<ArgumentException>(() => service.Save(recording, "Deploy"));
        service.Delete(saved.Id);
        Assert.Empty(service.List());
    }

    [Fact]
    public void PasswordAndTokenLiteralsCannotBecomeMacroCommands()
    {
        host.SshPassword = "secret-password";
        var recording = new MacroRecording(host); recording.Start();
        Assert.Throws<ArgumentException>(() => recording.Capture("tool --password=topsecret"));
        Assert.Throws<ArgumentException>(() => recording.Capture("echo secret-password"));
        Assert.Empty(recording.Commands);
    }

    [Fact]
    public async Task FailedTargetDoesNotPreventRemainingTargetsAndProgressShowsOutcomes()
    {
        dependencies.FailureHost = host.Id;
        var service = new MacroService(Config(), dependencies, dependencies);
        var macro = new MacroDefinition { Name = "health", Commands = ["uptime"] };
        var messages = new List<string>();
        var results = await service.Run(macro, [host, host, new() { Id = "two", Alias = "db" }], messages.Add);
        Assert.Equal(new[] { "one", "two" }, dependencies.Targets);
        Assert.False(results[0].Success); Assert.True(results[1].Success);
        Assert.Contains(messages, message => message.Contains("[1/2]") && message.Contains("FAILED"));
        Assert.Contains(messages, message => message.Contains("[2/2]") && message.Contains("completed"));
        Assert.Equal(2, dependencies.Events.Count(entry => entry.EventType == "macro.executed"));
    }

    [Fact]
    public async Task MacroUsesOneShellPreservesStateAndStopsBeforeNextCommandOnFailure()
    {
        dependencies.RunLocally = true;
        var service = new MacroService(Config(), dependencies, dependencies);
        var macro = new MacroDefinition { Name = "state", Commands = ["cd /", "export SSHOCKED_TEST_VALUE=retained", "printf '%s:%s' \"$PWD\" \"$SSHOCKED_TEST_VALUE\"", "false", "printf 'MUST_NOT_RUN'"] };
        var output = new List<string>();
        var result = Assert.Single(await service.Run(macro, [host], output.Add));
        Assert.False(result.Success); Assert.Equal(1, result.ExitCode);
        Assert.Contains("/:retained", string.Concat(output));
        Assert.DoesNotContain("MUST_NOT_RUN", string.Concat(output));
        Assert.Single(dependencies.Commands);
    }

    [Fact]
    public async Task CancellationStopsBeforeStartingAnotherTarget()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = new MacroService(Config(), dependencies, dependencies);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Run(new() { Commands = ["true"] }, [host], _ => { }, cancellation.Token));
        Assert.Empty(dependencies.Commands);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
