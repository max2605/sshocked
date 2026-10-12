using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Rendering;
using Sshocked.Interfaces;
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
            var input = new PromptInput("updated\r\rnewtag\r", () =>
            {
                var other = new JsonConfigRepository(path);
                var recents = new RecentConnectionService(other);
                recents.Record(host, saved);
                recents.ToggleFavorite(host, saved);
                other.Update(config => config.Hosts[0].SavedContainers[0].Image = "new-image-from-another-window");
            });
            var inner = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.Yes });
            AnsiConsole.Console = new PromptConsole(inner, input);
            var groups = new GroupManagementService(repository, NullLogger<GroupManagementService>.Instance);
            new ContainerService(repository, groups, new ConsoleHelper(), NullLogger<ContainerService>.Instance).Edit(host, saved);
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

    private sealed class PromptInput(string text, Action beforeFirstKey) : IAnsiConsoleInput
    {
        private readonly Queue<char> _keys = new(text);
        private bool _first = true;
        public bool IsKeyAvailable() => _keys.Count > 0;
        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            if (_first) { _first = false; beforeFirstKey(); }
            if (_keys.Count == 0) throw new InvalidOperationException("The test prompt requested unexpected additional input.");
            var character = _keys.Dequeue();
            return new ConsoleKeyInfo(character, character == '\r' ? ConsoleKey.Enter : ConsoleKey.A, false, false, false);
        }
        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
    }

    private sealed class PromptConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
    {
        public Profile Profile => inner.Profile;
        public IAnsiConsoleCursor Cursor => inner.Cursor;
        public IAnsiConsoleInput Input => input;
        public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
        public RenderPipeline Pipeline => inner.Pipeline;
        public void Clear(bool home) => inner.Clear(home);
        public void Write(IRenderable renderable) => inner.Write(renderable);
        public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
    }
    private sealed class ConsoleHelper : IConsoleHelperService
    {
        public void WaitForKey() => throw new InvalidOperationException("Container editing unexpectedly displayed a conflict.");
        public Task RunSsh(ServerHost host) => throw new NotSupportedException();
        public Task RunDockerExec(ServerHost host, ContainerModel container) => throw new NotSupportedException();
        public Task RunDockerLogs(ServerHost host, ContainerModel container) => throw new NotSupportedException();
    }
}
