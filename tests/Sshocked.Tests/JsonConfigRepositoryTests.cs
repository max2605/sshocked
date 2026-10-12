using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class JsonConfigRepositoryTests
{
    [Fact]
    public void IndependentRepositoriesSerializeTheWholeReadModifyWriteTransaction()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-multiple-repositories-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        try
        {
            var repositories = Enumerable.Range(0, 4).Select(i => new JsonConfigRepository(
                i % 2 == 0 ? path : Path.Combine(directory, ".", "config.json"))).ToArray();
            repositories[0].Save(new AppConfig());
            Parallel.For(0, 60, i => repositories[i % repositories.Length].Update(config =>
            {
                // Widen the former race between loading a snapshot and persisting it.
                Thread.Sleep(2);
                config.Groups.Add(new ServerGroup { Id = i.ToString() });
            }));
            var config = repositories[0].Load();
            Assert.Equal(60, config.Groups.Count);
            Assert.Equal(60, config.Groups.Select(g => g.Id).Distinct().Count());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SavedCallbacksCanStartAnotherRepositoryTransaction()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-repository-events-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        try
        {
            var first = new JsonConfigRepository(path);
            var other = new JsonConfigRepository(path);
            first.Save(new AppConfig());
            first.Saved += (_, _) =>
            {
                // A different thread cannot enter if Saved is raised while the named lock is held.
                var update = Task.Run(() => other.Update(config => config.Groups.Add(new ServerGroup { Id = "event" })));
                Assert.True(update.Wait(TimeSpan.FromSeconds(5)), "Saved callback must run after releasing the transaction lock.");
            };
            first.Update(config => config.Hosts.Add(new ServerHost { Id = "host" }));
            Assert.Single(other.Load().Groups);
            Assert.Single(other.Load().Hosts);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ConcurrentUpdatesAreAtomicAndMalformedConfigIsPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sshocked-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        try
        {
            var repository = new JsonConfigRepository(path);
            repository.Save(new AppConfig());
            Parallel.For(0, 40, i => repository.Update(c => c.Groups.Add(new ServerGroup { Id = i.ToString() })));
            Assert.Equal(40, repository.Load().Groups.Count);
            File.WriteAllText(path, "{broken");
            Assert.Throws<System.Text.Json.JsonException>(() => repository.Load());
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
