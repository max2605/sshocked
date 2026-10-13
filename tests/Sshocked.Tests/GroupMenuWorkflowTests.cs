using Microsoft.Extensions.DependencyInjection;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class GroupMenuWorkflowTests
{
    [Fact]
    public async Task EmptyGroupCreationReturnsImmediatelyWithoutAcknowledgementOrMutation()
    {
        await using var fixture = new MenuWorkflowFixture(new(), "\r");
        fixture.Script.Choose("Manage Groups");
        fixture.Script.Choose("Create Group");
        fixture.Script.Choose("Back");
        fixture.Script.Choose("Exit");
        await fixture.Provider.GetRequiredService<IMainMenuService>().Show();
        Assert.Empty(fixture.Repository.Load().Groups);
        Assert.Equal(0, fixture.ConsoleHelper.Waits);
    }

    [Theory]
    [InlineData("y\r", true)]
    [InlineData("n\r", false)]
    public async Task GroupDeletionRequiresConfirmationAndPreservesServerContainerData(string answer, bool deleted)
    {
        await using var fixture = new MenuWorkflowFixture(Config(), answer);
        fixture.Script.Choose("Operations");
        fixture.Script.Choose("Delete group");
        if (!deleted) fixture.Script.Choose("Back");
        fixture.Script.Choose("Back");
        await fixture.Provider.GetRequiredService<IGroupMenuService>().Browse();
        var config = fixture.Repository.Load();
        Assert.Equal(deleted ? 0 : 1, config.Groups.Count);
        var host = Assert.Single(config.Hosts);
        var container = Assert.Single(host.SavedContainers);
        Assert.Equal(deleted ? "" : "group", host.GroupId);
        Assert.Equal(deleted ? "" : "group", container.GroupId);
        Assert.True(host.IsFavorite);
        Assert.True(container.IsFavorite);
        Assert.Single(config.RecentConnections);
        Assert.True(fixture.Navigation.IsRoot);
    }

    [Fact]
    public async Task EmptyGroupRetainsManagementActionsAndBackUsesStandardMenus()
    {
        await using var fixture = new MenuWorkflowFixture(new() { Groups = [new() { Id = "group", Name = "Empty" }] });
        fixture.Script.Choose("Empty");
        fixture.Script.Choose("Back");
        fixture.Script.Choose("Back");
        await fixture.Provider.GetRequiredService<IGroupMenuService>().Browse();
        var actions = fixture.Script.Menus.Single(menu => menu.Title.Contains("Group Actions", StringComparison.Ordinal)).Entries;
        Assert.Contains(actions, entry => entry.Label == "Rename group");
        Assert.Contains(actions, entry => entry.Label == "Delete group");
        Assert.Contains(actions, entry => entry.ActionType == MenuActionType.Back && entry.Shortcut == 'b');
        Assert.True(fixture.Navigation.IsRoot);
    }

    private static AppConfig Config() => new()
    {
        Groups = [new() { Id = "group", Name = "Operations" }],
        Hosts = [new()
        {
            Id = "host", Alias = "host", GroupId = "group", IsFavorite = true,
            SavedContainers = [new() { Id = "container", Name = "app", GroupId = "group", IsFavorite = true }]
        }],
        RecentConnections = [new() { HostId = "host", LastConnectedAt = DateTimeOffset.UtcNow }]
    };
}
