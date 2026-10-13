namespace Sshocked.Services;

public sealed class ProcessOutputLimitException(int maximumBytes) : IOException($"Process output exceeded the {maximumBytes}-byte limit.");
