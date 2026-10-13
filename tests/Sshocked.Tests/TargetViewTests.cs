using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class TargetViewTests
{
    [Theory]
    [InlineData("prod AND web AND NOT maintenance", true)]
    [InlineData("PROD and (database OR web) and not staging", true)]
    [InlineData("prod AND NOT (web OR database)", false)]
    [InlineData("\"AND\" OR 'multi word'", true)]
    [InlineData("missing OR prod AND web", true)]
    [InlineData("NOT NOT prod", true)]
    public void ExpressionsHaveCaseInsensitiveTagsAndBooleanPrecedence(string expression, bool expected)
    {
        var predicate = TargetViewExpression.Compile(expression);
        Assert.Equal(expected, predicate([" production ", "PROD", "web", "multi word"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("prod web")]
    [InlineData("prod AND")]
    [InlineData("prod OR OR web")]
    [InlineData("(prod AND web")]
    [InlineData("prod)")]
    [InlineData("\"unterminated")]
    [InlineData("''")]
    public void InvalidExpressionsFailBeforeTheyCanBePersisted(string expression) =>
        Assert.Throws<ArgumentException>(() => TargetViewExpression.Compile(expression));

    [Fact]
    public async Task SavedViewsRefreshMembershipAndExcludedHostsNeverExecute()
    {
        var fake = new FleetTestDependencies();
        fake.Config.Hosts.AddRange([
            new() { Id = "old", Alias = "old", HostName = "original", Tags = ["prod", "web"] },
            new() { Id = "new", Alias = "new", HostName = "new", Tags = ["stage", "web"] },
            new() { Id = "excluded", Alias = "excluded", HostName = "excluded", Tags = ["prod", "web", "maintenance"] }
        ]);
        var views = new TargetViewService(fake, fake);
        var view = views.Save("Production web", "prod AND web AND NOT maintenance");
        Assert.Equal(["old"], views.Resolve(view.Id).Select(host => host.Id));
        fake.Config.Hosts[0].Tags = ["stage", "web"];
        fake.Config.Hosts[1].Tags = ["PROD", "WEB"];
        Assert.Equal(["new"], views.Resolve("production WEB").Select(host => host.Id));
        var plan = new BatchPlanService().Create(BatchOperation.ForCommand("echo hello"), views.Resolve(view.Id));
        await new BatchExecutorService(fake, fake, fake).Execute(plan);
        Assert.Equal(["new"], fake.Calls.Select(call => call.HostId));
        views.Save("Updated", "prod OR stage", view.Id);
        Assert.Equal(3, views.Resolve("updated").Count);
        views.Delete(view.Id);
        Assert.Empty(views.List());
        Assert.Equal(3, fake.Events.Count);
    }

    [Fact]
    public void ConfigRoundTripKeepsNamedViewsAndOlderConfigDefaultsToEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "config.json");
            File.WriteAllText(file, "{}");
            var repository = new JsonConfigRepository(file);
            Assert.Empty(repository.Load().TargetViews);
            repository.Update(config => config.TargetViews.Add(new() { Name = "fleet", Expression = "prod AND NOT maintenance" }));
            Assert.Equal("prod AND NOT maintenance", new JsonConfigRepository(file).Load().TargetViews.Single().Expression);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DuplicateNamesAndMissingEditTargetsAreRejected()
    {
        var fake = new FleetTestDependencies();
        var views = new TargetViewService(fake, fake);
        views.Save("Fleet", "prod");
        Assert.Throws<ArgumentException>(() => views.Save("fleet", "dev"));
        Assert.Throws<ArgumentException>(() => views.Save("Other", "dev", "missing"));
        Assert.Throws<ArgumentException>(() => views.Save("Invalid", "prod AND"));
        Assert.Single(views.List());
    }
}
