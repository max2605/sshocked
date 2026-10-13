using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class MacroManualServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sshocked-manual-macros-" + Guid.NewGuid().ToString("N"));
    private readonly MacroTestDependencies dependencies = new();
    private JsonConfigRepository Repository() => new(Path.Combine(directory, "appconfig.json"));
    private MacroService Service() => new(Repository(), dependencies, dependencies);

    [Fact]
    public void CreatePersistsExactCommandsWithoutConnectingOrAuditingCommandText()
    {
        var commands = new List<string> { "  printf '%s' \"ready\"  ", "printf first\nprintf second" };
        var saved = Service().Create(" deployment ", commands);
        commands[0] = "changed after saving";
        saved.Commands[1] = "changed returned object";

        var loaded = Service().Find("DEPLOYMENT");
        Assert.Equal("deployment", loaded.Name);
        Assert.Equal(["  printf '%s' \"ready\"  ", "printf first\nprintf second"], loaded.Commands);
        Assert.Null(loaded.RecordedOnHostId);
        Assert.Empty(dependencies.Commands);
        Assert.Equal(MacroEventNames.Created, Assert.Single(dependencies.Events).EventType);
        Assert.Equal("deployment", dependencies.Events[0].Action);
    }

    [Fact]
    public void EditPreservesRecordingIdentityAndConcurrentUnrelatedConfigChanges()
    {
        var host = new ServerHost { Id = "recording-host", Alias = "web" };
        var recording = new MacroRecording(host);
        recording.Start(); recording.Capture("uptime"); recording.Stop();
        var saved = Service().Save(recording, "health");
        var original = Service().Find(saved.Id);
        Repository().Update(config => config.Hosts.Add(new() { Id = "new-host", Alias = "added elsewhere" }));

        var updated = Service().Update(original, "HEALTH", ["df -h", "uptime"]);

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal(host.Id, updated.RecordedOnHostId);
        Assert.Equal(["df -h", "uptime"], Service().Find(updated.Id).Commands);
        Assert.Equal("new-host", Assert.Single(Repository().Load().Hosts).Id);
        Assert.Equal(MacroEventNames.Updated, dependencies.Events.Last().EventType);
    }

    [Fact]
    public void ConcurrentEditsAndDeletionCannotOverwriteOrRestoreTheOriginal()
    {
        var original = Service().Create("health", ["uptime"]);
        var latest = Service().Update(original, "health", ["df -h"]);
        var auditCount = dependencies.Events.Count;
        Assert.Throws<InvalidOperationException>(() => Service().Update(original, "health", ["free -m"]));
        Assert.Equal(["df -h"], Service().Find(original.Id).Commands);
        Assert.Equal(auditCount, dependencies.Events.Count);

        Service().Delete(latest.Id);
        Assert.Throws<InvalidOperationException>(() => Service().Update(latest, "health", ["uptime"]));
        Assert.Empty(Service().List());
    }

    [Fact]
    public void DuplicateCopiesCommandsAndRecordingProvenanceIntoANewIdentity()
    {
        var recording = new MacroRecording(new() { Id = "host" });
        recording.Start(); recording.Capture("uptime"); recording.Stop();
        var original = Service().Save(recording, "health");
        Repository().Update(config => config.Macros.Single().CreatedAt = DateTimeOffset.Parse("2000-01-01T00:00:00Z"));
        original = Service().Find(original.Id);
        var duplicate = Service().Duplicate(original, "health copy", ["uptime", "df -h"]);

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.NotEqual(original.CreatedAt, duplicate.CreatedAt);
        Assert.Equal(original.RecordedOnHostId, duplicate.RecordedOnHostId);
        Assert.Equal(["uptime"], Service().Find(original.Id).Commands);
        Assert.Equal(["uptime", "df -h"], Service().Find(duplicate.Id).Commands);
        Assert.Equal(MacroEventNames.Duplicated, dependencies.Events.Last().EventType);
        Assert.Empty(dependencies.Commands);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("health\n")]
    [InlineData("\thealth")]
    public void InvalidNamesNeverPersist(string name)
    {
        Assert.Throws<ArgumentException>(() => Service().Create(name, ["true"]));
        Assert.Empty(Service().List());
        Assert.Empty(dependencies.Events);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("printf '\0'")]
    [InlineData("tool --password=do-not-store")]
    public void InvalidCommandsNeverPersist(string command)
    {
        Assert.Throws<ArgumentException>(() => Service().Create("health", [command]));
        Assert.Empty(Service().List());
        Assert.Empty(dependencies.Events);
    }

    [Fact]
    public void LimitsAndCaseInsensitiveNamesAreValidatedForEverySavePath()
    {
        var service = Service();
        var original = service.Create(new string('a', MacroCommandPolicy.MaximumNameLength), [new string('b', MacroCommandPolicy.MaximumCommandLength)]);
        Assert.Throws<ArgumentException>(() => service.Create(new string('a', MacroCommandPolicy.MaximumNameLength + 1), ["true"]));
        Assert.Throws<ArgumentException>(() => service.Create("too long", [new string('b', MacroCommandPolicy.MaximumCommandLength + 1)]));
        Assert.Throws<ArgumentException>(() => service.Create("empty", []));
        var largest = service.Create("maximum count", Enumerable.Repeat("true", MacroCommandPolicy.MaximumCommands).ToArray());
        Assert.Throws<ArgumentException>(() => service.Update(largest, largest.Name,
            Enumerable.Repeat("true", MacroCommandPolicy.MaximumCommands + 1).ToArray()));
        Assert.Throws<ArgumentException>(() => service.Duplicate(original, "MAXIMUM COUNT", ["true"]));
        Assert.Throws<ArgumentException>(() => service.Update(original, "maximum COUNT", ["true"]));
        Assert.Equal(2, service.List().Count);
    }

    [Fact]
    public void DraftDoesNotMutateItsSourceAndInvalidChangesLeaveItUntouched()
    {
        var original = Service().Create("health", ["uptime", "df -h"]);
        var draft = new MacroDraft(original);
        draft.Move(1, 0);
        draft.Edit(1, "free -m");
        Assert.Throws<ArgumentException>(() => draft.Edit(0, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.Move(0, -1));
        Assert.Equal(["df -h", "free -m"], draft.Commands);
        Assert.Equal(["uptime", "df -h"], original.Commands);
        Assert.Equal(["uptime", "df -h"], Service().Find(original.Id).Commands);
    }

    [Fact]
    public async Task KnownHostPasswordsAreRejectedBeforeExecutionWhileOtherTargetsContinue()
    {
        var macro = Service().Create("check", ["printf '%s' 'cobalt-value-123'"]);
        var results = await Service().Run(macro,
            [new() { Id = "protected", Alias = "protected", SshPassword = "cobalt-value-123" }, new() { Id = "safe", Alias = "safe" }], _ => { });
        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
        Assert.Equal(["safe"], dependencies.Targets);
        Assert.DoesNotContain("cobalt-value-123", results[0].Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvedCredentialsAreValidatedBeforeTheOnlyExecutionPath(bool nonInteractive)
    {
        var service = Service();
        var macro = service.Create("check", ["printf '%s' 'cobalt-value-123'"]);
        dependencies.PreparedPassword = "cobalt-value-123";
        var hosts = new ServerHost[] { new() { Id = "host", Alias = "host" } };
        var result = Assert.Single(nonInteractive ? await service.RunNonInteractive(macro, hosts, _ => { })
            : await service.Run(macro, hosts, _ => { }));
        Assert.False(result.Success);
        Assert.Empty(dependencies.Commands);
        Assert.Equal(1, dependencies.PreparedValidations);
        Assert.Equal(nonInteractive, dependencies.LastNonInteractive);
        Assert.Null(dependencies.LastLease);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
