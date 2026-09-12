using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Tokendial.Core.Settings;
using Tokendial.Core.Model;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tests;

public class RenderedDockTests
{
    [AvaloniaFact]
    public void RenderedCellsMatchHoverTargetsOnAllEdges()
    {
        var window = new PanelWindow(["claude", "codex", "copilot", "cursor", "antigravity", "gemini", "glm", "grok", "opencode"]);
        try
        {
            window.Show();
            foreach (var control in window.GetVisualDescendants().OfType<Animatable>()) control.Transitions = null;
            window.Flash();
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                window.SetEdge(edge);
                window.UpdateLayout();
                var cellPanels = window.GetVisualDescendants().OfType<StackPanel>()
                    .Where(p => p.Width == Theme.CellWidth && p.Children.OfType<Avalonia.Controls.Panel>().Any()).ToList();
                Assert.Equal(9, cellPanels.Count);
                for (var i = 0; i < cellPanels.Count; i++)
                {
                    var actual = cellPanels[i];
                    var expected = DockGeometry.CellRect(window.LastLayout!, i);
                    var point = actual.TranslatePoint(default, window)!.Value;
                    Assert.Equal(expected.X, point.X, 3);
                    Assert.Equal(expected.Y, point.Y, 3);
                    Assert.Equal(expected.Width, actual.Bounds.Width, 3);
                    Assert.Equal(expected.Height, actual.Bounds.Height, 3);
                    Assert.Equal(i, DockGeometry.CellAt(window.LastLayout!,
                        point + new Vector(actual.Bounds.Width / 2, actual.Bounds.Height / 2)));
                }
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void ExpandedCapsuleResizesAcrossUnplugAndReconnectSnapshots()
    {
        var ids = Enumerable.Repeat("claude", 9).ToArray();
        var primary = new ScreenInfo("eDP-1", true, new(0, 0, 1920, 1080), new(0, 40, 1920, 1040), 1);
        var external = new ScreenInfo("DP-1", false, new(-2560, 0, 2560, 1440), new(-2560, 0, 2560, 1440), 2);
        var window = new PanelWindow(ids, "DP-1", DockEdge.Left);
        try
        {
            window.Show();
            foreach (var control in window.GetVisualDescendants().OfType<Animatable>()) control.Transitions = null;
            window.Flash();
            var capsule = Assert.IsType<Canvas>(Assert.IsType<Canvas>(window.Content).Children[0]);
            foreach (ScreenInfo[] attached in new[] { new[] { primary, external }, new[] { primary }, new[] { primary, external } })
            {
                var target = Displays.Choose(attached, "DP-1")!;
                window.Reposition(target, target.Scaling);
                window.UpdateLayout();
                var at = window.LastLayout!;
                Assert.Equal(at.CapsuleWidth, capsule.Width);
                Assert.Equal(at.CapsuleHeight, capsule.Height);
                Assert.Equal(at.CapsuleWidth, capsule.Bounds.Width);
                Assert.Equal(at.CapsuleHeight, capsule.Bounds.Height);
                Assert.InRange(at.Placement.X, target.WorkingArea.Left, target.WorkingArea.Right);
                Assert.True(at.Placement.Bottom <= target.WorkingArea.Bottom);
            }
        }
        finally { window.Close(); }
    }

}
