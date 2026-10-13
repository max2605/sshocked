using Microsoft.Extensions.DependencyInjection;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class ServerMenuWorkflowTests
{
    [Fact]
    public async Task BackFromServerBrowserRestoresParentNavigation()
    {
        await using var fixture = new MenuWorkflowFixture(new() { Hosts = [Host()] });
        fixture.Navigation.Push(ViewType.GroupDetail);
        fixture.Script.Choose("Back");
        await fixture.Provider.GetRequiredService<IServerMenuService>().SelectServer();
        Assert.Equal(ViewType.GroupDetail, fixture.Navigation.Current);
        Assert.Single(fixture.Script.Menus);
    }

    [Fact]
    public async Task FavoritesAndRecentsUseTheExactManagementServerActionsAtRoot()
    {
        var host = Host();
        await using var fixture = new MenuWorkflowFixture(new()
        {
            Hosts = [host],
            RecentConnections = [new() { HostId = host.Id, LastConnectedAt = DateTimeOffset.UtcNow }]
        });
        fixture.Script.Choose("Back");
        await fixture.Provider.GetRequiredService<IServerMenuService>().ShowServerActions(host);
        var expected = fixture.Script.Menus[0].Entries.Select(entry => entry.Label).ToArray();
        foreach (var recent in new[] { false, true })
        {
            fixture.Script.Choose(host.Alias);
            fixture.Script.Choose("Back");
            fixture.Script.Choose("Back");
            var quickAccess = fixture.Provider.GetRequiredService<IQuickAccessMenuService>();
            if (recent) await quickAccess.ShowRecent(); else await quickAccess.ShowFavorites();
            Assert.True(fixture.Navigation.IsRoot);
        }
        var actionMenus = fixture.Script.Menus.Where(menu => menu.Title.Contains("Server Actions", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, actionMenus.Count);
        Assert.All(actionMenus, menu => Assert.Equal(expected, menu.Entries.Select(entry => entry.Label)));
        Assert.Contains("Edit", expected);
        Assert.Contains("Delete", expected);
        Assert.Contains("Upload file", expected);
        Assert.Contains("Run command / record macro", expected);
    }

    [Fact]
    public async Task SingleServerActionsDispatchCommandsAndMacrosOnlyToSelectedServer()
    {
        var host = Host();
        await using var fixture = new MenuWorkflowFixture(new() { Hosts = [host, new() { Id = "other", Alias = "other" }] });
        fixture.Script.Choose("Run command / record macro");
        fixture.Script.Choose("Run macro");
        fixture.Script.Choose("Back");
        await fixture.Provider.GetRequiredService<IServerMenuService>().ShowServerActions(host);
        Assert.Equal(["single-command", "macro"], fixture.Targets.Calls.Select(call => call.Action));
        Assert.All(fixture.Targets.Calls, call => Assert.Equal([host.Id], call.HostIds));
        Assert.True(fixture.Navigation.IsRoot);
    }

    [Fact]
    public async Task ServerDeletionFromAnotherWindowExitsContextAndRestoresParent()
    {
        var host = Host();
        await using var fixture = new MenuWorkflowFixture(new() { Hosts = [host] });
        fixture.Navigation.Push(ViewType.GroupDetail);
        fixture.Script.Choose("Remove favorite", () => fixture.Repository.Update(config => config.Hosts.Clear()));
        await fixture.Provider.GetRequiredService<IServerMenuService>().ShowServerActions(host);
        Assert.Equal(ViewType.GroupDetail, fixture.Navigation.Current);
        Assert.Single(fixture.Script.Menus);
    }

    private static ServerHost Host() => new() { Id = "server", Alias = "primary", HostName = "server.example", User = "operator", IsFavorite = true };
}
