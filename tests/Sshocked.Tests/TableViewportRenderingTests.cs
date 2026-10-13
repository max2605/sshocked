using System.Text;
using Spectre.Console;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

public sealed class TableViewportRenderingTests
{
    [Fact]
    public void GroupTablesKeepTheirBordersAlignedWhenWindowIsMaximizedAndRestored()
    {
        var writer = new StringWriter();
        var viewportWidth = 120;
        var viewportHeight = 30;
        var output = new TerminalConsoleOutput(new BufferedOutput(writer),
            () => viewportWidth, () => viewportHeight, true);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = output,
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors
        });
        console.Profile.Capabilities.Unicode = true;
        var hosts = new List<ServerHost>
        {
            new()
            {
                Id = "admin", Alias = "games-admin", HostName = "games.example.test",
                User = "ubuntu", IsFavorite = true,
                Tags = ["admin", "ssh-key", "a-long-tag-that-needs-to-wrap"],
                SavedContainers = [new() { Name = "容器-container", Image = "registry.example.test/image:long-version" }]
            },
            new()
            {
                Id = "password", Alias = "server-with-password", HostName = "another.long.hostname.example.test",
                User = "operator", AuthType = AuthType.Password,
                Tags = ["passphrase-dpapi", "management-commands-only"]
            }
        };
        var config = new AppConfig
        {
            Hosts = hosts,
            UI = new() { EnableReachabilityChecks = false },
            RecentConnections = [new() { HostId = "admin", LastConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-19) }]
        };
        var renderer = new TableRendererService(new TableRendererTestsFakeReachability(),
            new TableRendererTestsMemoryRepository(config));

        foreach (var width in new[] { 120, 240, 80, 120 })
        {
            viewportWidth = width;
            viewportHeight = width == 240 ? 60 : 30;
            foreach (var group in new[] { hosts.Take(1).ToList(), hosts, hosts.Skip(1).ToList() })
            {
                writer.GetStringBuilder().Clear();
                console.Write(renderer.BuildTable("Group with wrapped cells", group, config));
                var lines = writer.ToString().Replace("\r\n", "\n").Split('\n');

                Assert.All(lines, line => Assert.True(line.GetCellWidth() < width,
                    $"A rendered line occupies {line.GetCellWidth()} cells in a {width}-column viewport."));
                var rows = lines.Where(line => line.StartsWith('│')).ToList();
                Assert.NotEmpty(rows);
                var expectedColumns = BorderColumns(rows[0]);
                Assert.Equal(11, expectedColumns.Length);
                Assert.All(rows, row => Assert.Equal(expectedColumns, BorderColumns(row)));
            }
        }
    }

    private static int[] BorderColumns(string row) => row.Select((character, index) => (character, index))
        .Where(item => item.character == '│').Select(item => row[..item.index].GetCellWidth()).ToArray();

    private sealed class BufferedOutput(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => 400;
        public int Height => 9000;
        public void SetEncoding(Encoding encoding) { }
    }
}
