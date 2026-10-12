using Sshocked.Interfaces;

namespace Sshocked.Services;

public sealed class ArgumentParserService : IArgumentParserService
{
    public ParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParseResult();
        }

        var first = args[0].ToLowerInvariant();
        if (first is "history" or "audit")
            return new ParseResult { ManagementCommand = first, CommandArguments = args[1..] };

        if (first is "--help" or "-h")
        {
            return new ParseResult { ShowHelp = true };
        }

        if (first is "--version" or "-v")
        {
            return new ParseResult { ShowVersion = true };
        }

        if (first is "--list" or "-l")
        {
            return new ParseResult { ShowList = true };
        }

        if (first is "--group" or "-g")
        {
            if (args.Length < 3)
            {
                return new ParseResult { ShowHelp = true };
            }

            var groupName = args[1];
            var commandStart = 2;

            if (commandStart < args.Length && args[commandStart] == "--")
            {
                commandStart++;
            }

            var command = string.Join(" ", args.Skip(commandStart));

            if (string.IsNullOrWhiteSpace(command))
            {
                return new ParseResult { ShowHelp = true };
            }

            return new ParseResult
            {
                HasGroupCommand = true,
                GroupName = groupName,
                Command = command
            };
        }

        return new ParseResult
        {
            HasPositionalArg = true,
            PositionalArg = args[0]
        };
    }
}
