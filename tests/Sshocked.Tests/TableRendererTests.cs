using Spectre.Console;
using Sshocked.Interfaces;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class TableRendererTests
{
    [Fact]
    public void TableEscapesMarkupAndRendersFavoriteDockerAndRecentIndicators()
    {
        var host = new ServerHost
        {
            Id = "host", Alias = "alias[red]", HostName = "host.example", User = "admin", IsFavorite = true,
            SavedContainers = [new() { Name = "container[green]", Image = "image[blue]", ContainerId = "container", IsFavorite = true }]
        };
        var config = new AppConfig { Hosts = [host], RecentConnections = [new() { HostId = "host", LastConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-2) }] };
        var reachability = new FakeReachability();
        var renderer = new TableRendererService(reachability, new MemoryRepository(config));
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors });
        console.Profile.Width = 200;
        console.Write(renderer.BuildTable("group[cyan]", [host], config));
        var text = output.ToString();
        Assert.Contains("group[cyan]", text);
        Assert.Contains("alias[red]", text);
        Assert.Contains("container[green]", text);
        Assert.Contains("★", text);
        Assert.Contains("D:1", text);
        Assert.Contains("2m ago", text);
        Assert.Equal(0, reachability.Scheduled);
    }

    [Fact]
    public void CompactModeHidesContainerRowsAndOptionalColumns()
    {
        var host = new ServerHost { Alias = "compact", HostName = "host", User = "admin", SavedContainers = [new() { Name = "hidden-container" }] };
        var config = new AppConfig { UI = new() { CompactMode = true, ShowLastConnectedColumn = false, ShowReachabilityStatus = false } };
        var renderer = new TableRendererService(new FakeReachability(), new MemoryRepository(config));
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 200;
        console.Write(renderer.BuildTable("group", [host], config));
        Assert.DoesNotContain("hidden-container", output.ToString());
        Assert.DoesNotContain("Last connected", output.ToString());
        Assert.DoesNotContain("Status", output.ToString());
    }

    private sealed class FakeReachability : IHostReachabilityService
    {
        public int Scheduled { get; private set; }
        public HostReachability GetStatus(ServerHost host, bool scheduleCheck = false) { if (scheduleCheck) Scheduled++; return new(HostReachabilityState.Unchecked); }
        public Task<HostReachability> CheckPortOpen(ServerHost host, int timeoutMs = 1000, CancellationToken ct = default) => Task.FromResult(new HostReachability(HostReachabilityState.Unchecked));
    }
    private sealed class MemoryRepository(AppConfig config) : IConfigRepository
    {
        public AppConfig Load() => config;
        public void Save(AppConfig updated) { }
    }
}
