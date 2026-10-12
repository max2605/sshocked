using Sshocked.Interfaces;
using Sshocked.Models;


namespace Sshocked.Tests;

internal sealed class SessionRecordingTestsConfig : IConfigRepository
{
    public AppConfig Value { get; private set; } = new();

    public AppConfig Load() => Value;
    public void Save(AppConfig config) => Value = config;
}
