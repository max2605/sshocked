using Microsoft.Extensions.Logging.Abstractions;

using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ConnectionHookTests
{
    private static HookScript Script(string command) => new() { Mode = HookExecutionMode.ShellCommand, Command = command };
    private static ServerHost Host() => new() { Id = "id", Alias = "web with spaces", HostName = "server", User = "alice", Port = 2222, Tags = ["Prod", "web"] };
    private static HookExecutorService Executor(ConnectionHookTestsRunner runner, ConnectionHookTestsAudit? audit = null) =>
        new(runner, audit ?? new ConnectionHookTestsAudit(), NullLogger<HookExecutorService>.Instance);

    [Fact]
    public void GlobalTagsAreCaseInsensitiveAndExclusionsTakePrecedence()
    {
        var hooks = new GlobalHooks { IncludeTags = ["prod"] };
        Assert.True(HookDefinition.MatchesTags(hooks, Host()));
        hooks.ExcludeTags = ["WEB"];
        Assert.False(HookDefinition.MatchesTags(hooks, Host()));
        hooks.ExcludeTags.Clear();
        hooks.IncludeTags = ["database"];
        Assert.False(HookDefinition.MatchesTags(hooks, Host()));
        hooks.IncludeTags.Clear();
        Assert.True(HookDefinition.MatchesTags(hooks, Host()));
    }

    [Fact]
    public void HooksUseGlobalBeforeHostForPreAndHostBeforeGlobalForPost()
    {
        var global = Script("global");
        var specific = Script("host");
        var host = Host();
        host.Hooks = new() { PreConnect = specific, PostConnect = specific, PreDisconnect = specific, PostDisconnect = specific };
        var hooks = new GlobalHooks { PreConnect = global, PostConnect = global, PreDisconnect = global, PostDisconnect = global };
        Assert.Equal(new[] { global, specific }, HookDefinition.GetOrderedHooks(HookPhase.PreConnect, host, hooks));
        Assert.Equal(new[] { global, specific }, HookDefinition.GetOrderedHooks(HookPhase.PreDisconnect, host, hooks));
        Assert.Equal(new[] { specific, global }, HookDefinition.GetOrderedHooks(HookPhase.PostConnect, host, hooks));
        Assert.Equal(new[] { specific, global }, HookDefinition.GetOrderedHooks(HookPhase.PostDisconnect, host, hooks));
        specific.Enabled = false;
        Assert.Same(global, Assert.Single(HookDefinition.GetOrderedHooks(HookPhase.PreConnect, host, hooks)));
    }

    [Fact]
    public void EnvironmentIncludesMetadataWithoutPasswordAndPreventsMetadataOverride()
    {
        var host = Host();
        host.SshPassword = "secret-never-export";
        var hook = Script("test");
        hook.Environment = new() { ["CUSTOM"] = "hello", ["SSHOCKED_HOST_ALIAS"] = "override" };
        var timestamp = DateTimeOffset.Parse("2026-10-12T10:00:00Z");
        var environment = HookDefinition.BuildEnvironment(host, HookPhase.PostDisconnect,
            new HookContext { GroupName = "Production", ConnectionTimestamp = timestamp, ConfigDirectory = "config", ExitCode = 7, DurationSeconds = 12.5 }, hook);
        Assert.Equal("post-disconnect", environment["SSHOCKED_HOOK_TYPE"]);
        Assert.Equal(host.Alias, environment["SSHOCKED_HOST_ALIAS"]);
        Assert.Equal("2222", environment["SSHOCKED_HOST_PORT"]);
        Assert.Equal("Production", environment["SSHOCKED_HOST_GROUP"]);
        Assert.Equal("7", environment["SSHOCKED_EXIT_CODE"]);
        Assert.Equal("12.5", environment["SSHOCKED_SESSION_DURATION_SECONDS"]);
        Assert.Equal("hello", environment["CUSTOM"]);
        Assert.DoesNotContain("secret-never-export", environment.Values);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 2)]
    public async Task BlockingFailureAbortsUnlessContinueOnError(bool continueOnError, bool expected, int count)
    {
        var host = Host();
        host.Hooks = new() { PreConnect = Script("host") };
        var global = new GlobalHooks { PreConnect = Script("global") };
        global.PreConnect.ContinueOnError = continueOnError;
        var runner = new ConnectionHookTestsRunner { Handler = (_, _) => Task.FromResult(new ProcessResult(1, "", "failed")) };
        host.Hooks.PreConnect!.ContinueOnError = true;
        Assert.Equal(expected, await Executor(runner).RunLocalHooks(HookPhase.PreConnect, host, global, new()));
        Assert.Equal(count, runner.Calls.Count);
    }

    [Fact]
    public async Task TimeoutCancelsChildAndAuditsFailure()
    {
        var host = Host();
        host.Hooks = new() { PreConnect = Script("slow") };
        host.Hooks.PreConnect.TimeoutSeconds = 1;
        var audit = new ConnectionHookTestsAudit();
        var runner = new ConnectionHookTestsRunner
        {
            Handler = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(0, "", ""); }
        };
        Assert.False(await Executor(runner, audit).RunLocalHooks(HookPhase.PreConnect, host, null, new()));
        Assert.False(Assert.Single(audit.Entries).Success);
        Assert.Null(audit.Entries[0].ExitCode);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedForBlockingHook()
    {
        var host = Host();
        host.Hooks = new() { PreConnect = Script("slow") };
        var runner = new ConnectionHookTestsRunner
        {
            Handler = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(0, "", ""); }
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Executor(runner).RunLocalHooks(HookPhase.PreConnect, host, null, new(), cancellation.Token));
    }

    [Fact]
    public async Task NonblockingHookReturnsWhileChildStillRunsAndObservesFailure()
    {
        var host = Host();
        host.Hooks = new() { PreConnect = Script("background") };
        host.Hooks.PreConnect.Blocking = false;
        var completion = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ConnectionHookTestsRunner { Handler = (_, _) => completion.Task };
        var audit = new ConnectionHookTestsAudit();
        Assert.True(await Executor(runner, audit).RunLocalHooks(HookPhase.PreConnect, host, null, new()));
        Assert.Empty(audit.Entries);
        completion.SetResult(new(1, "", "failed"));
        await audit.Recorded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Assert.Single(audit.Entries).Success);
    }

    [Fact]
    public async Task InlineScriptIsPrivateAndRemovedAfterExecution()
    {
        var host = Host();
        host.Hooks = new() { PreConnect = new() { Mode = HookExecutionMode.InlineScript, InlineScript = "echo hello" } };
        string? scriptPath = null;
        var runner = new ConnectionHookTestsRunner
        {
            Handler = (info, _) =>
            {
                scriptPath = info.ArgumentList.First(argument => argument.Contains("sshocked-hook-"));
                Assert.True(File.Exists(scriptPath));
                Assert.Equal("echo hello", File.ReadAllText(scriptPath));
                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(scriptPath));
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
        };
        Assert.True(await Executor(runner).RunLocalHooks(HookPhase.PreConnect, host, null, new()));
        Assert.NotNull(scriptPath);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public async Task ServiceDisposalCancelsBackgroundHookAndRemovesTemporaryScript()
    {
        var host = Host();
        host.Hooks = new() { PostDisconnect = new() { Mode = HookExecutionMode.InlineScript, InlineScript = "echo background", Blocking = false } };
        string? scriptPath = null;
        var runner = new ConnectionHookTestsRunner
        {
            Handler = async (info, token) =>
            {
                scriptPath = info.ArgumentList.First(argument => argument.Contains("sshocked-hook-"));
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new(0, "", "");
            }
        };
        var executor = Executor(runner);
        Assert.True(await executor.RunLocalHooks(HookPhase.PostDisconnect, host, null, new()));
        Assert.True(File.Exists(scriptPath));
        await executor.DisposeAsync();
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void LocalScriptArgumentsExpandMetadataAsSingleArguments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sshocked-script-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(path, "Write-Output test");
            var hook = new HookScript { Mode = HookExecutionMode.LocalScript, ScriptPath = path, Arguments = "-Message '${SSHOCKED_HOST_ALIAS}'" };
            var environment = HookDefinition.BuildEnvironment(Host(), HookPhase.PreConnect, new());
            var info = HookLocalProcess.BuildLocalStartInfo(hook, environment, out var temporary);
            Assert.Null(temporary);
            Assert.Equal("web with spaces", info.ArgumentList[^1]);
            Assert.Equal("-Message", info.ArgumentList[^2]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemoteWrapperPreservesStateAndOrdersCleanupAroundInteractiveShell()
    {
        var host = Host();
        host.Hooks = new()
        {
            PostConnect = new() { Mode = HookExecutionMode.RemoteCommand, Command = "cd '/var/log'; export TEST=value" },
            PreDisconnect = new() { Mode = HookExecutionMode.RemoteCommand, Command = "echo host-cleanup", ContinueOnError = true }
        };
        var global = new GlobalHooks
        {
            PostConnect = new() { Mode = HookExecutionMode.RemoteCommand, Command = "echo global-setup" },
            PreDisconnect = new() { Mode = HookExecutionMode.RemoteCommand, Command = "echo global-cleanup" }
        };
        var command = Executor(new ConnectionHookTestsRunner()).BuildRemoteSessionCommand(host, global, new())!;
        var hostSetup = command.IndexOf("/var/log", StringComparison.Ordinal);
        var globalSetup = command.IndexOf("global-setup", StringComparison.Ordinal);
        var shell = command.IndexOf("\"${SHELL:-/bin/sh}\" -i", StringComparison.Ordinal);
        var globalCleanup = command.IndexOf("global-cleanup", StringComparison.Ordinal);
        var hostCleanup = command.IndexOf("host-cleanup", StringComparison.Ordinal);
        Assert.True(hostSetup < globalSetup && globalSetup < shell && shell < globalCleanup && globalCleanup < hostCleanup);
        Assert.Contains("export SSHOCKED_EXIT_CODE=\"$_sshocked_session_exit\"", command);
        Assert.EndsWith("exit \"$_sshocked_session_exit\"\n", command);
        Assert.DoesNotContain('\r', command);
    }

    [Theory]
    [InlineData(HookPhase.PreConnect)]
    [InlineData(HookPhase.PostDisconnect)]
    public void RemoteCommandsAreRejectedForLocalOnlyPhases(HookPhase phase) => Assert.Throws<ArgumentException>(() =>
        HookDefinition.Validate(new HookScript { Mode = HookExecutionMode.RemoteCommand, Command = "true" }, phase));

    [Theory]
    [InlineData("bad-name")]
    [InlineData("x; rm")]
    [InlineData("1name")]
    public void RemoteEnvironmentRejectsShellSyntaxInVariableNames(string name)
    {
        var hook = Script("test");
        hook.Environment[name] = "value";
        Assert.Throws<ArgumentException>(() => HookDefinition.Validate(hook, HookPhase.PreConnect));
    }

    [Fact]
    public void RemoteQuoteHandlesApostropheAndHomeWithoutEvaluatingOtherSyntax()
    {
        Assert.Equal("'a'\"'\"'b; $(touch /tmp/nope)'", RemoteShell.Quote("a'b; $(touch /tmp/nope)"));
        Assert.Equal("\"$HOME\"/'a'\"'\"'b'", RemoteShell.QuotePath("~/a'b"));
    }
}
