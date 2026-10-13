namespace Sshocked.Tests;

// Real shell startup, pipe readers and process cleanup share a five-second test
// budget. Keep unrelated native-child tests from starving these lifecycle checks.
[CollectionDefinition("Managed command sessions", DisableParallelization = true)]
public sealed class ManagedCommandSessionCollection;
