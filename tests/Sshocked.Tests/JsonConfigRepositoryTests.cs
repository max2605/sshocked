using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class JsonConfigRepositoryTests
{
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
