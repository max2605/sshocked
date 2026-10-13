using Spectre.Console;
using Spectre.Console.Rendering;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class TableHostResolutionRenderingTests
{
    [Fact]
    public void AddressesAndDnsErrorsRemainSeparateFromConfiguredHostAndTcpStatus()
    {
        var hosts = new List<ServerHost>
        {
            new() { Alias = "multi", HostName = "server[red].example", User = "user" },
            new() { Alias = "error", HostName = "missing.example", User = "user" },
            new() { Alias = "literal", HostName = "192.0.2.8", User = "user" }
        };
        var config = new AppConfig { Hosts = hosts, UI = new() { EnableReachabilityChecks = false } };
        var resolution = new TableRendererTestsFakeResolution();
        resolution.Results[hosts[0].HostName] = new(HostResolutionState.Resolved, ["192.0.2.1", "2001:db8::1"]);
        resolution.Results[hosts[1].HostName] = new(HostResolutionState.Error, Error: "Name not found [red]");
        resolution.Results[hosts[2].HostName] = new(HostResolutionState.Resolved, ["192.0.2.8"]);
        var reachability = new TableRendererTestsFakeReachability();
        var renderer = new TableRendererService(reachability, new TableRendererTestsMemoryRepository(config), resolution);

        var table = renderer.BuildTable("group", hosts, config);

        Assert.Equal("Host", PlainText(table.Columns[2].Header));
        Assert.Equal("Host resolution", PlainText(table.Columns[3].Header));
        Assert.Equal("User", PlainText(table.Columns[4].Header));
        Assert.Equal("missing.example", PlainText(table.Rows.ElementAt(0)[2]));
        Assert.Equal("Name not found [red]", PlainText(table.Rows.ElementAt(0)[3]));
        Assert.Equal("192.0.2.8", PlainText(table.Rows.ElementAt(1)[3]));
        Assert.Equal("server[red].example", PlainText(table.Rows.ElementAt(2)[2]));
        Assert.Equal("192.0.2.1\n2001:db8::1", PlainText(table.Rows.ElementAt(2)[3]));
        Assert.Equal(3, resolution.Scheduled);
        Assert.Equal(0, reachability.Scheduled);
        Assert.All(table.Rows, row => Assert.Contains("unknown", PlainText(row[0])));
    }

    [Theory]
    [InlineData(HostResolutionState.Unchecked, "Not checked")]
    [InlineData(HostResolutionState.Resolving, "Resolving…")]
    [InlineData(HostResolutionState.Error, "DNS timeout")]
    public void PendingAndFailedLookupsHaveClearDisplayText(HostResolutionState state, string expected)
    {
        var host = new ServerHost { Alias = "host", HostName = "host.example" };
        var config = new AppConfig { Hosts = [host], UI = new() { ShowReachabilityStatus = false } };
        var resolution = new TableRendererTestsFakeResolution();
        resolution.Results[host.HostName] = new(state, Error: "DNS timeout");
        var renderer = new TableRendererService(new TableRendererTestsFakeReachability(),
            new TableRendererTestsMemoryRepository(config), resolution);

        Assert.Equal(expected, PlainText(renderer.BuildTable("group", [host], config).Rows.ElementAt(0)[2]));
    }

    [Theory]
    [InlineData(HostResolutionState.Resolved, "192.0.2.1\n(refreshing)")]
    [InlineData(HostResolutionState.Error, "Name not found\n(refreshing)")]
    public void CacheRefreshKeepsThePreviousResultVisible(HostResolutionState state, string expected)
    {
        var host = new ServerHost { Alias = "host", HostName = "host.example" };
        var config = new AppConfig { Hosts = [host], UI = new() { ShowReachabilityStatus = false } };
        var resolution = new TableRendererTestsFakeResolution();
        resolution.Results[host.HostName] = new(state, ["192.0.2.1"], "Name not found", IsRefreshing: true);
        var renderer = new TableRendererService(new TableRendererTestsFakeReachability(),
            new TableRendererTestsMemoryRepository(config), resolution);

        Assert.Equal(expected, PlainText(renderer.BuildTable("group", [host], config).Rows.ElementAt(0)[2]));
    }

    [Fact]
    public void HiddenColumnDoesNotScheduleDnsAndContainerRowsLeaveResolutionBlank()
    {
        var host = new ServerHost
        {
            Alias = "host", HostName = "host.example", User = "user",
            SavedContainers = [new() { Name = "container", Image = "image" }]
        };
        var config = new AppConfig { Hosts = [host], UI = new() { ShowReachabilityStatus = false } };
        var resolution = new TableRendererTestsFakeResolution();
        var renderer = new TableRendererService(new TableRendererTestsFakeReachability(),
            new TableRendererTestsMemoryRepository(config), resolution);

        var shown = renderer.BuildTable("group", [host], config);
        Assert.Equal("image", PlainText(shown.Rows.ElementAt(1)[1]));
        Assert.Equal("", PlainText(shown.Rows.ElementAt(1)[2]));
        Assert.Equal("user", PlainText(shown.Rows.ElementAt(0)[3]));
        var lookups = resolution.Requested.Count;

        config.UI.ShowHostResolution = false;
        var hidden = renderer.BuildTable("group", [host], config);
        Assert.Equal(lookups, resolution.Requested.Count);
        Assert.DoesNotContain(hidden.Columns, column => PlainText(column.Header) == "Host resolution");
        Assert.Equal("user", PlainText(hidden.Rows.ElementAt(0)[2]));
    }

    [Fact]
    public void RefreshTrackingFollowsTheVisibleGroupAndStopsForHiddenOrEmptyViews()
    {
        var first = new ServerHost { Alias = "first", HostName = "first.example" };
        var second = new ServerHost { Alias = "second", HostName = "second.example" };
        var config = new AppConfig { Hosts = [first, second], UI = new() { ShowReachabilityStatus = false } };
        var resolution = new TableRendererTestsFakeResolution { Revision = 7, RefreshDue = true };
        var renderer = new TableRendererService(new TableRendererTestsFakeReachability(),
            new TableRendererTestsMemoryRepository(config), resolution);
        var previous = AnsiConsole.Console;
        using var output = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        AnsiConsole.Profile.Width = 240;
        try
        {
            renderer.RenderServerTable(config);
            Assert.Equal(7, renderer.ViewRevision);
            Assert.True(renderer.RefreshDue);
            Assert.Equal([first.HostName, second.HostName], resolution.VisibleHostNames);
            Assert.Contains("local DNS", output.ToString());

            renderer.ResetViewRefresh();
            Assert.Equal(0, renderer.ViewRevision);
            Assert.False(renderer.RefreshDue);
            Assert.Empty(resolution.VisibleHostNames);

            renderer.RenderGroup("one", [first]);
            Assert.True(renderer.RefreshDue);
            Assert.Equal([first.HostName], resolution.VisibleHostNames);

            config.UI.ShowHostResolution = false;
            renderer.RenderGroup("one", [first]);
            Assert.False(renderer.RefreshDue);
            Assert.Equal(0, renderer.ViewRevision);
            Assert.Empty(resolution.VisibleHostNames);

            config.UI.ShowHostResolution = true;
            config.Hosts.Clear();
            renderer.RenderServerTable(config);
            Assert.False(renderer.RefreshDue);
            Assert.Empty(resolution.VisibleHostNames);
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    private static string PlainText(IRenderable content)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors
        });
        console.Profile.Width = 500;
        console.Write(content);
        return output.ToString().Replace("\r\n", "\n");
    }
}
