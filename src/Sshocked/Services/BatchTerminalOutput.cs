using System.Text;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public static class BatchTerminalOutput
{
    public static void Write(IConsoleWriterService console, BatchTarget target, string output)
    {
        var alias = Sanitize(target.Alias).Replace('\n', ' ').Replace('\t', ' ');
        var text = Sanitize(output);
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index == lines.Length - 1 && lines[index].Length == 0) break;
            console.WriteLine($"[{alias}] {lines[index]}");
        }
    }

    public static string Sanitize(string value)
    {
        var output = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '\x1b' or '\x9b' or '\x9d')
            {
                var sequence = character == '\x1b' ? ++index < value.Length ? value[index] : '\0' : character == '\x9b' ? '[' : ']';
                if (sequence == '[')
                {
                    while (++index < value.Length && value[index] is not (>= '@' and <= '~')) { }
                }
                else if (sequence is ']' or 'P' or '^' or '_')
                {
                    while (++index < value.Length)
                    {
                        if (value[index] is '\x07' or '\x9c') break;
                        if (value[index] == '\x1b' && index + 1 < value.Length && value[index + 1] == '\\') { index++; break; }
                    }
                }
                else while (index < value.Length && value[index] is >= ' ' and <= '/') index++;
                continue;
            }
            if (character == '\r')
            {
                if (index + 1 < value.Length && value[index + 1] == '\n') index++;
                output.Append('\n');
            }
            else if (!char.IsControl(character) || character is '\n' or '\t') output.Append(character);
        }
        return output.ToString();
    }
}
