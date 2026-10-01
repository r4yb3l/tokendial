using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Tokendial.Core.Model;
using Tokendial.Core.Settings;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tests;

/// <summary>
/// The dock following the store and the settings, headless: which providers it draws and whether it is on
/// screen at all. Pointer-driven expansion and the X11 map and unmap stay a hardware check.
/// </summary>
public sealed class PanelWindowModelTests
{
    private static PanelModel Model(params string[] ids) => new(
        ids.Select((id, index) => new Tile(id, id, Tile.MarkFor(id),
            new ProviderReading(id, id, Fidelity.Official, ReadingStatus.LiveNow,
                [new UsageWindow("fixture", "5h limit", (index + 1) / 10.0)]),
            null, null, false)).ToList(),
        [], 0);

    /// <summary>The name under each expanded cell's dial, in the order the cells are drawn.</summary>
    private static string CellNames(PanelWindow window) => string.Join(",",
        window.GetVisualDescendants().OfType<StackPanel>()
            .Where(p => p.Width == Theme.CellWidth && p.Children.OfType<Avalonia.Controls.Panel>().Any())
            .Select(p => p.Children.OfType<TextBlock>().First().Text));

    [AvaloniaFact]
    public void DisconnectingAndConnectingRebuildsTheDock()
    {
        var window = new PanelWindow(["claude", "codex", "opencode"]);
        try
        {
            window.Show();
            window.Update(Model("claude", "codex", "opencode"));
            Assert.Equal("claude,codex,opencode", CellNames(window));
            Assert.Equal(3, window.LastLayout!.Count);
            var three = window.Width;

            window.Update(Model("claude", "opencode"));
            Assert.Equal("claude,opencode", CellNames(window));
            Assert.Equal(2, window.LastLayout!.Count);

            window.Update(Model("claude", "codex", "opencode", "cursor"));
            Assert.Equal("claude,codex,opencode,cursor", CellNames(window));
            Assert.Equal(4, window.LastLayout!.Count);
            Assert.True(window.Width > three);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ProvidersThatChangedBeforeTheDockWasShownAreTheOnesItDraws()
    {
        var window = new PanelWindow(["claude"]);
        try
        {
            window.Update(Model("claude", "codex"));
            window.Show();
            Assert.Equal("claude,codex", CellNames(window));
            Assert.Equal(2, window.LastLayout!.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ModesDecideWhetherTheDockIsShownAndOpen()
    {
        var window = new PanelWindow(["claude", "codex"]);
        var held = new List<bool>();
        window.HoverChanged += held.Add;
        try
        {
            window.SetMode(PanelMode.Hidden);
            Assert.False(window.IsVisible);

            window.SetMode(PanelMode.ExpandOnHover);
            Assert.True(window.IsVisible);
            Assert.False(window.IsExpanded);

            // Open without anyone reading it, so the alert engine is not told to stay quiet.
            window.SetMode(PanelMode.AlwaysExpanded);
            Assert.True(window.IsExpanded);
            Assert.Empty(held);

            window.SetMode(PanelMode.ExpandOnHover);
            Assert.False(window.IsExpanded);

            window.SetMode(PanelMode.Hidden);
            Assert.False(window.IsVisible);
            Assert.False(window.IsExpanded);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FlashingAHiddenDockShowsItWithoutChangingTheMode()
    {
        var window = new PanelWindow(["claude", "codex"]);
        var held = new List<bool>();
        window.HoverChanged += held.Add;
        try
        {
            window.SetMode(PanelMode.ExpandOnHover);
            window.Update(Model("claude", "codex"));
            window.SetMode(PanelMode.Hidden);
            window.Flash();

            Assert.True(window.IsVisible);
            Assert.True(window.IsExpanded);
            Assert.Equal(PanelMode.Hidden, window.Mode);
            Assert.True(Assert.Single(held));
            // Shown a second time with the same window: the cells are the ones already built, not a second set.
            Assert.Equal("claude,codex", CellNames(window));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Headless has no X connection, which is the state a Wayland session is in: the pointer poll reads nothing
    /// there, so the pin running out is the only thing that can put a flashed dock back and let the alert engine
    /// speak again.
    /// </summary>
    [AvaloniaFact]
    public void AFlashedHiddenDockGoesBackWhenThePinRunsOut()
    {
        var window = new PanelWindow(["claude", "codex"]);
        var held = new List<bool>();
        window.HoverChanged += held.Add;
        try
        {
            window.Update(Model("claude", "codex"));
            window.SetMode(PanelMode.Hidden);
            window.Flash();
            Assert.True(window.IsVisible);
            Assert.True(window.IsExpanded);

            window.Unpin();

            Assert.False(window.IsVisible);
            Assert.False(window.IsExpanded);
            Assert.Equal(PanelMode.Hidden, window.Mode);
            Assert.Equal(new[] { true, false }, held);
        }
        finally { window.Close(); }
    }
}
