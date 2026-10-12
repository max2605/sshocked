namespace Sshocked.Services;

internal static class HookConstants
{
    public const string PreConnect = "pre-connect";
    public const string PostConnect = "post-connect";
    public const string PreDisconnect = "pre-disconnect";
    public const string PostDisconnect = "post-disconnect";
    public const string EnvironmentPrefix = "SSHOCKED_";
    public const string EnvironmentNamePattern = @"^[A-Za-z_][A-Za-z0-9_]*$";
    public const string EnvironmentPlaceholderPattern = @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}";
    public const string HookType = "SSHOCKED_HOOK_TYPE";
    public const string HostId = "SSHOCKED_HOST_ID";
    public const string HostAlias = "SSHOCKED_HOST_ALIAS";
    public const string HostName = "SSHOCKED_HOST_NAME";
    public const string HostPort = "SSHOCKED_HOST_PORT";
    public const string HostUser = "SSHOCKED_HOST_USER";
    public const string HostGroup = "SSHOCKED_HOST_GROUP";
    public const string HostTags = "SSHOCKED_HOST_TAGS";
    public const string ConnectionTimestamp = "SSHOCKED_CONNECTION_TIMESTAMP";
    public const string ConfigDirectory = "SSHOCKED_CONFIG_DIR";
    public const string ExitCode = "SSHOCKED_EXIT_CODE";
    public const string SessionDuration = "SSHOCKED_SESSION_DURATION_SECONDS";
    public const string AuditCategory = "hook";
    public const string ScheduledEvent = "hook-scheduled";
    public const int DefaultTimeoutSeconds = 30;
    public const int MaximumTimeoutSeconds = 86400;
}
