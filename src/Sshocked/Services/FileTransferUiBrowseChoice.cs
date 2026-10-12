using Sshocked.Models;

namespace Sshocked.Services;

internal sealed record FileTransferUiBrowseChoice(string Label, RemoteFileEntry? Entry);
