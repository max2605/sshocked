using System.Text;
using Sshocked.Models;

namespace Sshocked.Services;

public static class MacroScriptBuilder
{
    public static string Build(MacroDefinition macro)
    {
        if (macro.Commands.Count is < 1 or > MacroCommandPolicy.MaximumCommands)
            throw new ArgumentException("A macro must contain from 1 to 1000 commands.");
        var script = new StringBuilder("set -eu\n");
        // eval keeps cd, exported variables and shell functions in one shell. Strict mode
        // plus an explicit status check prevents later commands from masking a failure.
        foreach (var command in macro.Commands)
        {
            MacroCommandPolicy.Validate(command);
            script.Append("eval ").Append(RemoteShell.Quote(command)).Append(" </dev/null\n")
                .Append("_sshocked_macro_status=$?\n")
                .Append("[ \"$_sshocked_macro_status\" -eq 0 ] || exit \"$_sshocked_macro_status\"\n");
        }
        return "sh -c " + RemoteShell.Quote(script.ToString());
    }
}
