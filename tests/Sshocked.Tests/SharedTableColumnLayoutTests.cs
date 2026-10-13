using System.Text;
using System.Text.Json;
using Spectre.Console;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class SharedTableColumnLayoutTests
{
    [Theory]
    [InlineData(true, true, true, "Dark")]
    [InlineData(false, true, false, "Light")]
    [InlineData(true, false, true, "Light")]
    [InlineData(false, false, false, "Dark")]
    public void EveryGroupUsesTheSameColumnBoundariesAcrossViewportChanges(bool status, bool recents, bool resolution, string theme)
    {
        var config = CreateConfig(status, recents, theme);
        config.UI.ShowHostResolution = resolution;
        using var fixture = new ViewFixture(config);
        var original = JsonSerializer.Serialize(fixture.Config);
        var frames = new List<int[]>();
        var columnCount = 7 + (status ? 1 : 0) + (recents ? 1 : 0) + (resolution ? 1 : 0);

        foreach (var width in new[] { 120, 240, 80, 120 })
        {
            var lines = fixture.Render(width);
            var rows = lines.Where(line => line.StartsWith('│')).ToArray();
            Assert.NotEmpty(rows);
            var boundaries = BorderColumns(rows[0]);
            Assert.Equal(columnCount + 1, boundaries.Length);
            Assert.All(rows, row => Assert.Equal(boundaries, BorderColumns(row)));
            Assert.All(lines, line => Assert.True(line.GetCellWidth() < width,
                $"A rendered line occupies {line.GetCellWidth()} cells in a {width}-column viewport."));
            Assert.Contains(lines, line => line.Contains("Uncategorized", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("Small [red]", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("A long group title", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("Empty group", StringComparison.Ordinal));
            frames.Add(boundaries);
        }

        Assert.Equal(frames[0], frames[3]);
        Assert.NotEqual(frames[0][^1], frames[1][^1]);
        Assert.NotEqual(frames[0][^1], frames[2][^1]);
        Assert.Equal(status ? fixture.Config.Hosts.Count * 4 : 0, fixture.Reachability.Scheduled);
        Assert.Equal(resolution ? fixture.Config.Hosts.Count * 4 : 0, fixture.Resolution.Scheduled);
        Assert.Equal(original, JsonSerializer.Serialize(fixture.Config));
    }

    [Fact]
    public void SavedContainerCellsParticipateInLayoutAndWrappedValuesRemainComplete()
    {
        var config = CreateConfig(true, true, "Dark");
        config.Hosts[1].SavedContainers = [new()
        {
            Name = "container-" + new string('c', 70),
            Image = "registry.example.test/" + new string('i', 80)
        }];
        using var fixture = new ViewFixture(config);
        var lines = fixture.Render(240);
        var rows = lines.Where(line => line.StartsWith('│')).ToArray();
        var boundariesWithContainer = BorderColumns(rows[0]);
        var aliasCells = string.Concat(rows.Select(row => row.Split('│')[2].Trim()));
        var hostCells = string.Concat(rows.Select(row => row.Split('│')[3].Trim()));
        Assert.Contains(config.Hosts[1].SavedContainers[0].Name, aliasCells);
        Assert.Contains(config.Hosts[1].SavedContainers[0].Image, hostCells);
        Assert.All(rows, row => Assert.Equal(boundariesWithContainer, BorderColumns(row)));

        config.Hosts[1].SavedContainers.Clear();
        var withoutContainer = fixture.Render(240).First(line => line.StartsWith('│'));
        Assert.NotEqual(boundariesWithContainer[2], BorderColumns(withoutContainer)[2]);
    }

    [Fact]
    public void CompactViewsShareHeaderPositionsAndKeepSavedContainersHidden()
    {
        var config = CreateConfig(false, false, "Light");
        config.UI.CompactMode = true;
        config.Hosts[1].SavedContainers = [new() { Name = "hidden-container", Image = new string('x', 150) }];
        using var fixture = new ViewFixture(config);
        foreach (var width in new[] { 120, 240, 80, 120 })
        {
            var lines = fixture.Render(width);
            var headers = lines.Where(line => line.Contains("Alias", StringComparison.Ordinal)
                && line.Contains("Host", StringComparison.Ordinal) && line.Contains("User", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, headers.Length);
            var positions = HeaderColumns(headers[0]);
            Assert.All(headers, header => Assert.Equal(positions, HeaderColumns(header)));
            Assert.All(lines, line => Assert.True(line.GetCellWidth() < width));
            Assert.DoesNotContain(lines, line => line.Contains("hidden-container", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("TCP status", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("Last connected", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AFilteredGroupMeasuresOnlyTheHostsInItsView()
    {
        var config = CreateConfig(false, false, "Dark");
        using var fixture = new ViewFixture(config);
        fixture.Render(240);
        fixture.Writer.GetStringBuilder().Clear();
        fixture.Renderer.RenderGroup("Filtered", [config.Hosts[1]]);
        var text = fixture.Writer.ToString();
        Assert.Contains("Filtered", text);
        Assert.Contains("short-host", text);
        Assert.DoesNotContain("long-group-host", text);
        Assert.DoesNotContain("unknown-group-host", text);
        Assert.All(SplitLines(text), line => Assert.True(line.GetCellWidth() < 240));
    }

    private static AppConfig CreateConfig(bool status, bool recents, string theme) => new()
    {
        UI = new() { ShowReachabilityStatus = status, ShowLastConnectedColumn = recents, Theme = theme },
        Groups =
        [
            new() { Id = "small", Name = "Small [red]" },
            new() { Id = "long", Name = "A long group title whose length does not determine the column layout" },
            new() { Id = "empty", Name = "Empty group" }
        ],
        Hosts =
        [
            new() { Id = "orphan", Alias = "orphan", GroupId = "missing", HostName = "unknown-group-host", User = "u" },
            new() { Id = "small", Alias = "short", GroupId = "small", HostName = "short-host", User = "ubuntu" },
            new()
            {
                Id = "long", Alias = "a-much-longer-alias", GroupId = "long", HostName = "long-group-host.example.test",
                User = "long-user", Port = 2222, AuthType = AuthType.Password,
                Tags = ["a-very-long-tag-that-wraps-in-a-narrow-window", "tag[cyan]"]
            }
        ],
        RecentConnections = [new() { HostId = "long", LastConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-10) }]
    };

    private static int[] BorderColumns(string row) => row.Select((character, index) => (character, index))
        .Where(item => item.character == '│').Select(item => row[..item.index].GetCellWidth()).ToArray();

    private static int[] HeaderColumns(string row) => new[] { "Alias", "Host", "User", "Port", "Auth", "Docker", "Tags" }
        .Select(header => row[..row.IndexOf(header, StringComparison.Ordinal)].GetCellWidth()).ToArray();

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private sealed class ViewFixture : IDisposable
    {
        private readonly IAnsiConsole previous = AnsiConsole.Console;
        private int width = 120;
        public StringWriter Writer { get; } = new();
        public AppConfig Config { get; }
        public TableRendererTestsFakeReachability Reachability { get; } = new();
        public TableRendererTestsFakeResolution Resolution { get; } = new();
        public TableRendererService Renderer { get; }

        public ViewFixture(AppConfig config)
        {
            Config = config;
            var output = new TerminalConsoleOutput(new BufferedOutput(Writer), () => width, () => 60, true);
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = output, Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
                // CI profile enrichers can force ANSI despite the plain-text settings.
                Enrichment = new() { UseDefaultEnrichers = false }
            });
            console.Profile.Capabilities.Unicode = true;
            AnsiConsole.Console = console;
            Renderer = new(Reachability, new TableRendererTestsMemoryRepository(config), Resolution);
        }

        public string[] Render(int viewportWidth)
        {
            width = viewportWidth;
            Writer.GetStringBuilder().Clear();
            Renderer.RenderServerTable(Config);
            return SplitLines(Writer.ToString());
        }

        public void Dispose()
        {
            AnsiConsole.Console = previous;
            Writer.Dispose();
        }
    }

    private sealed class BufferedOutput(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => 400;
        public int Height => 9000;
        public void SetEncoding(Encoding encoding) { }
    }
}
