using Spectre.Console;
using Sshocked.Models;
using Sshocked.Services;

namespace Sshocked.Tests;

[Collection("Console capabilities")]
public sealed class UiThemePaletteTests
{
    [Fact]
    public void LegacyThemeNamesAlwaysResolveToTheDarkPalette()
    {
        var light = UiThemePalette.For("light");
        var dark = UiThemePalette.For("Dark");
        Assert.Equal("Dark", light.Name);
        Assert.Equal(Color.Black, light.BodyStyle.Background);
        Assert.Equal(dark.BodyStyle.Foreground, light.BodyStyle.Foreground);
        Assert.Equal(Color.Black, dark.BodyStyle.Background);
        Assert.Equal(light.SelectionStyle, dark.SelectionStyle);
        Assert.Equal(light.Success, dark.Success);
        Assert.Equal(light.Warning, dark.Warning);
        Assert.Equal(light.Error, dark.Error);
    }

    [Fact]
    public void MenuMarkupUsesSemanticColorsAndPreservesEscapedHostMetadata()
    {
        var mapped = UiThemePalette.For("Light").AdaptMarkup("[bold yellow]Manage[/] [grey]host[[green]][/] [cyan]Connect[/]");
        Assert.Contains("[bold yellow]", mapped);
        Assert.Contains("[grey70]", mapped);
        Assert.Contains("host[[green]]", mapped);
        Assert.Contains("[cyan]Connect", mapped);
        Assert.NotNull(new Markup(mapped));
    }

    [Fact]
    public void ReachabilityStatesHaveClearTextAndValidThemeMarkup()
    {
        foreach (var theme in new[] { "Light", "Dark" })
            foreach (var state in Enum.GetValues<HostReachabilityState>())
            {
                var badge = HostVisuals.ReachabilityBadge(new(state), theme);
                Assert.NotNull(new Markup(badge));
                Assert.DoesNotContain("?", badge);
            }
        Assert.Contains("unknown", HostVisuals.ReachabilityBadge(new(HostReachabilityState.Unchecked)));
        Assert.Contains("checking", HostVisuals.ReachabilityBadge(new(HostReachabilityState.Checking)));
        Assert.Contains("expired", HostVisuals.ReachabilityBadge(new(HostReachabilityState.Stale)));
    }

    [Fact]
    public void SemanticTextColorsHaveAtLeastFourPointFiveContrastAgainstTheirBackground()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var palette = UiThemePalette.For(theme);
            foreach (var color in new[] { palette.Text, palette.Accent, palette.Muted, palette.Success, palette.Warning, palette.Error })
            {
                var style = Style.Parse($"{color} on {palette.Background}");
                var first = Luminance(style.Foreground);
                var second = Luminance(style.Background);
                var contrast = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
                Assert.True(contrast >= 4.5, $"{theme} color {color} has contrast {contrast}");
            }
        }
    }

    [Fact]
    public void LegacyLightSettingCannotChangeTheDarkTheme()
    {
        var config = new AppConfig { UI = new() { ShowBreadcrumbs = false, ShowFooter = false } };
        var chrome = new UiChromeService(new TableRendererTestsMemoryRepository(config));
        var foreground = AnsiConsole.Foreground;
        var background = AnsiConsole.Background;
        var interactive = AnsiConsole.Profile.Capabilities.Interactive;
        AnsiConsole.Profile.Capabilities.Interactive = false;
        try
        {
            chrome.RenderFooter();
            Assert.Equal(Color.Black, AnsiConsole.Background);
            config.UI.Theme = "Light";
            chrome.RenderBreadcrumb("Hidden");
            Assert.Equal("Dark", config.UI.Theme);
            Assert.Equal(Color.Black, AnsiConsole.Background);
            Assert.Equal(Style.Parse("silver").Foreground, AnsiConsole.Foreground);
            chrome.Dispose();
            Assert.Equal(foreground, AnsiConsole.Foreground);
            Assert.Equal(background, AnsiConsole.Background);
        }
        finally
        {
            AnsiConsole.Foreground = foreground;
            AnsiConsole.Background = background;
            AnsiConsole.Profile.Capabilities.Interactive = interactive;
        }
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
