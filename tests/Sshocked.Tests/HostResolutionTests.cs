using System.Net;
using System.Net.Sockets;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class HostResolutionTests
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("[::1]", "::1")]
    [InlineData(" 192.0.2.1 ", "192.0.2.1")]
    public void LiteralAddressesDoNotUseDns(string hostName, string expected)
    {
        var resolver = new Resolver((_, _) => throw new InvalidOperationException("DNS must not be called"));
        using var service = new HostResolutionService(resolver);

        var result = service.GetResolution(hostName, scheduleCheck: true);

        Assert.Equal(HostResolutionState.Resolved, result.State);
        Assert.Equal([expected], result.Addresses);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, service.Revision);
        Assert.False(service.IsRefreshDue([hostName]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void EmptyHostShowsAnErrorWithoutScheduling(string hostName)
    {
        var resolver = new Resolver((_, _) => throw new InvalidOperationException("DNS must not be called"));
        using var service = new HostResolutionService(resolver);

        var result = service.GetResolution(hostName, scheduleCheck: true);

        Assert.Equal(HostResolutionState.Error, result.State);
        Assert.Equal("Invalid host", result.Error);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task SchedulingDoesNotRunBlockingResolverOnTheCaller()
    {
        var entered = Signal();
        var released = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new Resolver((_, _) =>
        {
            entered.TrySetResult();
            return Task.FromResult(released.Task.GetAwaiter().GetResult());
        });
        using var service = new HostResolutionService(resolver);

        try
        {
            // A dedicated caller isolates the responsiveness assertion from pool contention
            // caused by other process/console tests running concurrently.
            var scheduled = Task.Factory.StartNew(() => service.GetResolution("example.test", scheduleCheck: true),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var result = await scheduled.WaitAsync(TimeSpan.FromSeconds(1));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(HostResolutionState.Resolving, result.State);
            Assert.Equal(HostResolutionState.Resolving, service.GetResolution("example.test").State);
        }
        finally { released.TrySetResult([IPAddress.Loopback]); }

        await Completed(service, "example.test");
    }

    [Fact]
    public async Task ReturnsAllDistinctIpv4AndIpv6AddressesWithOneLookupPerHostname()
    {
        var pending = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new Resolver((_, _) => pending.Task);
        using var service = new HostResolutionService(resolver);

        Assert.Equal(HostResolutionState.Unchecked, service.GetResolution("server.test").State);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(HostResolutionState.Resolving, service.GetResolution("SERVER.test", true).State);
        service.GetResolution("server.TEST", true);
        await Until(() => resolver.Calls == 1);
        pending.SetResult([IPAddress.Parse("2001:db8::2"), IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.1")]);

        var result = await Completed(service, "server.test");

        Assert.Equal(HostResolutionState.Resolved, result.State);
        Assert.Equal(["192.0.2.1", "2001:db8::2"], result.Addresses);
        Assert.NotNull(result.LastCheckedUtc);
        Assert.False(result.IsRefreshing);
        Assert.Equal(1, service.Revision);
        service.GetResolution("SERVER.TEST", true);
        Assert.Equal(1, resolver.Calls);
    }

    [Theory]
    [InlineData(SocketError.HostNotFound, "Name not found")]
    [InlineData(SocketError.NoData, "No IP addresses")]
    [InlineData(SocketError.TimedOut, "DNS timeout")]
    [InlineData(SocketError.TryAgain, "DNS unavailable")]
    [InlineData(SocketError.AccessDenied, "DNS error")]
    public async Task SocketFailuresHaveConciseMessagesAndAreCached(SocketError error, string expected)
    {
        var resolver = new Resolver((_, _) => Task.FromException<IPAddress[]>(new SocketException((int)error)));
        using var service = new HostResolutionService(resolver);
        service.GetResolution("missing.test", true);

        var result = await Completed(service, "missing.test");

        Assert.Equal(HostResolutionState.Error, result.State);
        Assert.Equal(expected, result.Error);
        Assert.Null(result.Addresses);
        service.GetResolution("missing.test", true);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task EmptyDnsResponseShowsNoIpAddresses()
    {
        var resolver = new Resolver((_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        using var service = new HostResolutionService(resolver);
        service.GetResolution("empty.test", true);

        var result = await Completed(service, "empty.test");

        Assert.Equal(HostResolutionState.Error, result.State);
        Assert.Equal("No IP addresses", result.Error);
    }

    [Theory]
    [InlineData(true, "Invalid host")]
    [InlineData(false, "DNS error")]
    public async Task ExceptionDetailsAreNotDisplayed(bool argumentException, string expected)
    {
        Exception exception = argumentException
            ? new ArgumentException("Synthetic private detail")
            : new InvalidOperationException("Synthetic private detail");
        var resolver = new Resolver((_, _) => Task.FromException<IPAddress[]>(exception));
        using var service = new HostResolutionService(resolver);
        service.GetResolution("bad.test", true);

        var result = await Completed(service, "bad.test");

        Assert.Equal(expected, result.Error);
        Assert.DoesNotContain("private", result.Error);
    }

    [Fact]
    public async Task TimeoutBoundsResolverThatIgnoresCancellation()
    {
        var pending = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken lookupToken = default;
        var resolver = new Resolver((_, token) => { lookupToken = token; return pending.Task; });
        using var service = new HostResolutionService(resolver, lookupTimeout: TimeSpan.FromMilliseconds(40));
        service.GetResolution("slow.test", true);

        var result = await Completed(service, "slow.test");

        Assert.Equal(HostResolutionState.Error, result.State);
        Assert.Equal("DNS timeout", result.Error);
        Assert.True(lookupToken.IsCancellationRequested);
        pending.SetResult([IPAddress.Loopback]);
        Assert.Equal("DNS timeout", service.GetResolution("slow.test").Error);
    }

    [Fact]
    public async Task ExpiredResultsRemainVisibleUntilRefreshCompletes()
    {
        var clock = new Clock();
        var next = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var resolver = new Resolver((_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromResult(new[] { IPAddress.Parse("192.0.2.1") }) : next.Task);
        using var service = new HostResolutionService(resolver, clock);
        service.GetResolution("refresh.test", true);
        var first = await Completed(service, "refresh.test");
        Assert.False(service.IsRefreshDue(["refresh.test"]));

        clock.Now = clock.Now.AddMinutes(5);
        Assert.True(service.IsRefreshDue(["REFRESH.test"]));
        Assert.False(service.IsRefreshDue(["other.test"]));
        var previous = service.GetResolution("refresh.test", true);
        Assert.Equal(HostResolutionState.Resolved, previous.State);
        Assert.Equal(first.Addresses, previous.Addresses);
        Assert.True(previous.IsRefreshing);
        Assert.False(service.IsRefreshDue(["refresh.test"]));
        next.SetResult([IPAddress.Parse("2001:db8::4")]);

        await Until(() => service.Revision == 2);
        var refreshed = service.GetResolution("refresh.test");
        Assert.Equal(["2001:db8::4"], refreshed.Addresses);
        Assert.Equal(clock.Now, refreshed.LastCheckedUtc);
        Assert.False(refreshed.IsRefreshing);
        Assert.False(service.IsRefreshDue(["refresh.test"]));
    }

    [Fact]
    public async Task ExpiredErrorsAreRefreshedAndPreviousErrorRemainsVisible()
    {
        var clock = new Clock();
        var next = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var resolver = new Resolver((_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)) : next.Task);
        using var service = new HostResolutionService(resolver, clock);
        service.GetResolution("refresh.test", true);
        await Completed(service, "refresh.test");
        clock.Now = clock.Now.AddMinutes(6);

        var previous = service.GetResolution("refresh.test", true);

        Assert.Equal(HostResolutionState.Error, previous.State);
        Assert.Equal("Name not found", previous.Error);
        Assert.True(previous.IsRefreshing);
        next.SetResult([IPAddress.Loopback]);
        await Until(() => service.Revision == 2);
        Assert.Equal(HostResolutionState.Resolved, service.GetResolution("refresh.test").State);
    }

    [Fact]
    public async Task NoMoreThanFourLookupsRunConcurrently()
    {
        var released = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new Resolver((_, token) => released.Task.WaitAsync(token));
        using var service = new HostResolutionService(resolver);
        var names = Enumerable.Range(0, 8).Select(index => $"host{index}.test").ToArray();
        foreach (var name in names) service.GetResolution(name, true);
        await Until(() => resolver.Calls == 4);

        Assert.Equal(4, resolver.Active);
        released.SetResult([IPAddress.Loopback]);
        await Until(() => service.Revision == 8);
        Assert.Equal(8, resolver.Calls);
        Assert.InRange(resolver.MaximumActive, 1, 4);
    }

    [Fact]
    public async Task DisposeCancelsPendingLookupsAndPreventsQueuedOrNewLookups()
    {
        var cancelled = Signal();
        var resolver = new Resolver(async (_, token) =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return [];
        });
        var service = new HostResolutionService(resolver);
        foreach (var index in Enumerable.Range(0, 8)) service.GetResolution($"host{index}.test", true);
        await Until(() => resolver.Calls == 4);

        service.Dispose();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Until(() => resolver.Active == 0);
        service.Dispose();
        Assert.Equal(4, resolver.Calls);
        Assert.Equal(HostResolutionState.Unchecked, service.GetResolution("new.test", true).State);
        Assert.False(service.IsRefreshDue(["host0.test"]));
        Assert.Equal(0, service.Revision);
    }

    private static async Task<HostResolution> Completed(HostResolutionService service, string name)
    {
        await Until(() => service.GetResolution(name).State is HostResolutionState.Resolved or HostResolutionState.Error);
        return service.GetResolution(name);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, deadline.Token);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Resolver(Func<string, CancellationToken, Task<IPAddress[]>> resolve) : IDnsResolver
    {
        private int _calls;
        private int _active;
        private int _maximumActive;
        public int Calls => Volatile.Read(ref _calls);
        public int Active => Volatile.Read(ref _active);
        public int MaximumActive => Volatile.Read(ref _maximumActive);

        public async Task<IPAddress[]> ResolveAsync(string hostName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _active);
            int maximum;
            do { maximum = Volatile.Read(ref _maximumActive); }
            while (active > maximum && Interlocked.CompareExchange(ref _maximumActive, active, maximum) != maximum);
            try { return await resolve(hostName, cancellationToken); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
