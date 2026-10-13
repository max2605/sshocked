using System.Text.Json;

namespace Sshocked.Models;

public sealed record BatchOperation(BatchOperationKind Kind, string? Command = null, string? MacroSnapshot = null,
    string? LocalPath = null, string? RemotePath = null, bool Recursive = false)
{
    public static BatchOperation ForCommand(string command) => new(BatchOperationKind.Command, Command: command);
    public static BatchOperation ForMacro(MacroDefinition macro) => new(BatchOperationKind.Macro,
        MacroSnapshot: JsonSerializer.Serialize(macro, AppConfigContext.Default.MacroDefinition));
    public static BatchOperation ForUpload(string local, string remote, bool recursive = false) =>
        new(BatchOperationKind.Upload, LocalPath: local, RemotePath: remote, Recursive: recursive);
    public static BatchOperation ForDownload(string remote, string localDirectory, bool recursive = false) =>
        new(BatchOperationKind.Download, LocalPath: localDirectory, RemotePath: remote, Recursive: recursive);

    internal MacroDefinition GetMacro() => JsonSerializer.Deserialize(MacroSnapshot!, AppConfigContext.Default.MacroDefinition)
        ?? throw new ArgumentException("Batch macro snapshot is invalid.");
}
