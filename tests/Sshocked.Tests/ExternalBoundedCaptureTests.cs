using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class ExternalBoundedCaptureTests
{
    [Fact]
    public async Task CombinedStdoutAndStderrBudgetAbortsTheLiveChild()
    {
        using var child = new ExternalBoundedTestChild();
        var info = child.Info("[Console]::Write('x'*600); [Console]::Error.Write('y'*600); Start-Sleep -Seconds 60",
            "printf '%600s' x; printf '%600s' y >&2; sleep 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(info, 1024, cancellationToken: cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            child.Release();
            await Assert.ThrowsAsync<ProcessOutputLimitException>(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(process.HasExited);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndlessOutputOnEitherPipeIsAbortedBeforeTheProcessCanHang(bool stderr)
    {
        using var child = new ExternalBoundedTestChild();
        var info = child.Info(stderr ? "while($true){[Console]::Error.Write('x'*4096)}" : "while($true){[Console]::Write('x'*4096)}",
            stderr ? "while :; do printf '%4096s' x >&2; done" : "while :; do printf '%4096s' x; done");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(info, 1024, cancellationToken: cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            child.Release();
            await Assert.ThrowsAsync<ProcessOutputLimitException>(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(process.HasExited);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    [Fact]
    public async Task ExactlyTheCombinedBudgetPreservesSeparateStreamsAndExitCode()
    {
        using var child = new ExternalBoundedTestChild();
        var info = child.Info("[Console]::Write('x'*512); [Console]::Error.Write('y'*512); exit 17",
            "printf '%512s' x; printf '%512s' y >&2; exit 17");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(info, 1024, cancellationToken: cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            child.Release();
            var result = await operation.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(17, result.ExitCode);
            Assert.Equal(512, result.StandardOutput.Length);
            Assert.Equal(512, result.StandardError.Length);
            Assert.True(process.HasExited);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    [Fact]
    public async Task Utf8LimitCountsBytesInsteadOfDecodedCharacters()
    {
        using var child = new ExternalBoundedTestChild();
        var info = child.Info("[Console]::Write(([string][char]233)*600); Start-Sleep -Seconds 60",
            "i=0; while [ $i -lt 600 ]; do printf '\\303\\251'; i=$((i+1)); done; sleep 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(info, 1024, cancellationToken: cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            child.Release();
            await Assert.ThrowsAsync<ProcessOutputLimitException>(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(process.HasExited);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    [Fact]
    public async Task CancellationKillsABoundedChildWhileItWaitsWithoutOutput()
    {
        using var child = new ExternalBoundedTestChild();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(child.Info("Start-Sleep -Seconds 60", "sleep 60"), 1024, cancellationToken: cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(process.HasExited);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    [Fact]
    public async Task NoInputClosesStdinAndNeverWaitsForAnInteractivePrompt()
    {
        using var child = new ExternalBoundedTestChild();
        var info = child.Info("if($null -ne [Console]::ReadLine()){exit 3}; [Console]::Write('eof')",
            "if read -r line; then exit 3; else printf eof; fi");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(info, 1024, cancellationToken: cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            child.Release();
            var result = await operation.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("eof", result.StandardOutput);
            Assert.True(info.RedirectStandardInput);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    [Fact]
    public async Task ProtectedInputIsDeliveredThroughStdinWithoutEnteringArgumentsOrCapturedOutput()
    {
        const string credential = "private-value-123";
        using var child = new ExternalBoundedTestChild();
        var info = child.Info("if([Console]::ReadLine() -ne $env:SSHOCKED_BOUND_EXPECTED){exit 3}; [Console]::Write('accepted')",
            "IFS= read -r line; [ \"$line\" = \"$SSHOCKED_BOUND_EXPECTED\" ] || exit 3; printf accepted");
        info.Environment["SSHOCKED_BOUND_EXPECTED"] = credential;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var operation = new ExternalProcessRunner().RunBounded(info, 1024, credential + "\n", cancellation.Token);
        try
        {
            using var process = await child.Ready(operation);
            child.Release();
            var result = await operation.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("accepted", result.StandardOutput);
            Assert.DoesNotContain(credential, string.Join(' ', info.ArgumentList));
            Assert.DoesNotContain(credential, result.StandardOutput + result.StandardError);
        }
        finally { cancellation.Cancel(); await Observe(operation); }
    }

    private static async Task Observe(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (ProcessOutputLimitException) { }
    }
}
