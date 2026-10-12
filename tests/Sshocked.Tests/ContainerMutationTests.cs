using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;


using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class ContainerMutationTests
{
    [Fact]
    public void ContainerEditRebasesOnlyEditedFieldsAfterOtherWindowWritesDuringPrompt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-container-edit-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "appconfig.json");
        var previousConsole = AnsiConsole.Console;
        try
        {
            var repository = new JsonConfigRepository(path);
            var saved = new ContainerModel { Id = "container", ContainerId = "docker-id", Alias = "old", Name = "app", Image = "old-image" };
            var host = new ServerHost { Id = "host", Alias = "host", SavedContainers = [saved] };
            repository.Save(new AppConfig { Hosts = [host] });
            var input = new ContainerMutationTestsPromptInput("updated\r\rnewtag\r", () =>
            {
                var other = new JsonConfigRepository(path);
                var recents = new RecentConnectionService(other);
                recents.Record(host, saved);
                recents.ToggleFavorite(host, saved);
                other.Update(config => config.Hosts[0].SavedContainers[0].Image = "new-image-from-another-window");
            });
            var inner = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes });
            AnsiConsole.Console = new ContainerMutationTestsPromptConsole(inner, input);
            var groups = new GroupManagementService(repository, NullLogger<GroupManagementService>.Instance);
            new ContainerService(repository, groups, new ContainerMutationTestsConsoleHelper(), NullLogger<ContainerService>.Instance).Edit(host, saved);
            var loaded = new JsonConfigRepository(path).Load();
            var container = Assert.Single(Assert.Single(loaded.Hosts).SavedContainers);
            Assert.Equal("updated", container.Alias);
            Assert.Equal(["newtag"], container.Tags);
            Assert.Equal("new-image-from-another-window", container.Image);
            Assert.True(container.IsFavorite);
            Assert.Equal(1, Assert.Single(loaded.RecentConnections).ConnectionCount);
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
