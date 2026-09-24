using Avalonia;
using Tokendial.Core.Model;
using Tokendial.Core.Settings;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tests;

/// <summary>
/// The geometry seam the real panel window drives through: four edges, hot zone and cell selection,
/// inward cards, fit/wrap, 0 and 9 providers, scales 1/1.25/2, negative origins, topology fallback.
/// Pure: no window, no X11, no pointer, no global Strings mutation.
/// </summary>
public sealed class DockGeometryTests
{
    private static readonly ScreenInfo Screen1080 = new("HDMI-1", true,
        new PixelBox(0, 0, 1920, 1080), new PixelBox(0, 40, 1920, 1000), 1.0);

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void AllEdges_PlaceInsideWorkArea(DockEdge edge)
    {
        var at = DockGeometry.Compute(Screen1080, edge, 3, expanded: false, renderScale: 1.0);

        var work = Screen1080.WorkingArea;
        Assert.True(at.Placement.X >= work.X - 1);
        Assert.True(at.Placement.Y >= work.Y - 1 || edge == DockEdge.Top && at.Placement.Y == work.Y);
        Assert.True(at.Placement.Right <= work.Right + 1 || edge is DockEdge.Top or DockEdge.Bottom);
        Assert.True(at.Placement.Bottom <= work.Bottom + 1 || edge is DockEdge.Left or DockEdge.Right);
    }

    [Fact]
    public void Top_DocksBelowTopPanel_CentredOnBounds()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Top, 3, expanded: false, renderScale: 1.0);

        Assert.Equal(40, at.Placement.Y);
        Assert.Equal((1920 - at.Placement.Width) / 2, at.Placement.X);
        Assert.Equal(0, at.CapsuleTop);
    }

    [Fact]
    public void Bottom_DocksAboveWorkBottom()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Bottom, 3, expanded: false, renderScale: 1.0);

        Assert.Equal(1040 - at.Placement.Height, at.Placement.Y);
        Assert.Equal((1920 - at.Placement.Width) / 2, at.Placement.X);
    }

    [Fact]
    public void Left_DocksAtWorkLeft_CentredOnWorkArea()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Left, 3, expanded: false, renderScale: 1.0);

        Assert.True(at.Vertical);
        Assert.Equal(0, at.Placement.X);
        Assert.Equal(40 + (1000 - at.Placement.Height) / 2, at.Placement.Y);
        Assert.Equal(0, at.CapsuleLeft);
        Assert.True(at.CapsuleHeight > at.CapsuleWidth);
    }

    [Fact]
    public void Right_DocksAtWorkRight()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Right, 3, expanded: false, renderScale: 1.0);

        Assert.True(at.Vertical);
        Assert.Equal(1920 - at.Placement.Width, at.Placement.X);
        Assert.Equal(at.WindowWidth - at.CapsuleWidth, at.CapsuleLeft);
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void HotZone_ExtendsTowardInteriorOnly(DockEdge edge)
    {
        var at = DockGeometry.Compute(Screen1080, edge, 3, expanded: false, renderScale: 1.0);

        var capsule = new Rect(at.CapsuleLeft, at.CapsuleTop, at.CapsuleWidth, at.CapsuleHeight);
        var inward = edge switch
        {
            DockEdge.Top => new Point(capsule.X + capsule.Width / 2, capsule.Bottom + Theme.HotZone - 1),
            DockEdge.Bottom => new Point(capsule.X + capsule.Width / 2, capsule.Y - Theme.HotZone + 1),
            DockEdge.Left => new Point(capsule.Right + Theme.HotZone - 1, capsule.Y + capsule.Height / 2),
            _ => new Point(capsule.X - Theme.HotZone + 1, capsule.Y + capsule.Height / 2)
        };
        var outward = edge switch
        {
            DockEdge.Top => new Point(capsule.X + capsule.Width / 2, capsule.Y - 1),
            DockEdge.Bottom => new Point(capsule.X + capsule.Width / 2, capsule.Bottom + 1),
            DockEdge.Left => new Point(capsule.X - 1, capsule.Y + capsule.Height / 2),
            _ => new Point(capsule.Right + 1, capsule.Y + capsule.Height / 2)
        };
        Assert.True(at.Hot.Contains(capsule.Center), "capsule itself is hot");
        Assert.True(at.Hot.Contains(inward), $"interior strip is hot on {edge}");
        Assert.False(at.Hot.Contains(outward), $"exterior is not hot on {edge}");
    }

    [Fact]
    public void CellSelection_TopRow_RoundTrips()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Top, 3, expanded: true, renderScale: 1.0);

        Assert.Equal(1, at.Grid.Secondary);
        for (var i = 0; i < 3; i++)
        {
            var cell = DockGeometry.CellRect(at, i);
            Assert.True(cell.Width == Theme.CellWidth && cell.Height == Theme.CellHeight);
            Assert.Equal(i, DockGeometry.CellAt(at, cell.Center));
        }
        // Same row, upright: every cell shares the row's band.
        var first = DockGeometry.CellRect(at, 0);
        var last = DockGeometry.CellRect(at, 2);
        Assert.Equal(first.Y, last.Y);
        Assert.True(last.X > first.X);
    }

    [Fact]
    public void CellSelection_HotZoneBeyondCells_IsNoCell()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Top, 3, expanded: true, renderScale: 1.0);

        var belowCells = new Point(at.CapsuleLeft + at.CapsuleWidth / 2,
            at.CapsuleTop + at.CapsuleHeight + Theme.HotZone - 1);
        Assert.True(at.Hot.Contains(belowCells));
        Assert.Equal(-1, DockGeometry.CellAt(at, belowCells));
        Assert.Equal(-1, DockGeometry.CellAt(at, new Point(-5, -5)));
    }

    [Fact]
    public void CellSelection_LeftColumn_RunsDown()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Left, 3, expanded: true, renderScale: 1.0);

        Assert.Equal(1, at.Grid.Secondary);
        Assert.Equal(3, at.Grid.PerPrimary);
        for (var i = 0; i < 3; i++)
            Assert.Equal(i, DockGeometry.CellAt(at, DockGeometry.CellRect(at, i).Center));
        var first = DockGeometry.CellRect(at, 0);
        var last = DockGeometry.CellRect(at, 2);
        Assert.Equal(first.X, last.X);
        Assert.True(last.Y > first.Y);
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void CardAnchor_OpensTowardInterior(DockEdge edge)
    {
        var at = DockGeometry.Compute(Screen1080, edge, 3, expanded: true, renderScale: 1.0);
        var cell = DockGeometry.CellRect(at, 1);
        var anchor = DockGeometry.CardAnchor(at, cell, 1.0);

        var cellTopPx = at.Placement.Y + (int)(cell.Y * 1.0);
        var cellBottomPx = at.Placement.Y + (int)((cell.Y + cell.Height) * 1.0);
        var cellLeftPx = at.Placement.X + (int)(cell.X * 1.0);
        var cellRightPx = at.Placement.X + (int)((cell.X + cell.Width) * 1.0);
        switch (edge)
        {
            case DockEdge.Top:
                Assert.True(anchor.Y >= cellBottomPx, "below the cell");
                Assert.InRange(anchor.X, (cellLeftPx + cellRightPx) / 2 - 2, (cellLeftPx + cellRightPx) / 2 + 2);
                break;
            case DockEdge.Bottom:
                Assert.True(anchor.Y <= cellTopPx, "above the cell");
                break;
            case DockEdge.Left:
                Assert.True(anchor.X >= cellRightPx, "right of the cell");
                break;
            default:
                Assert.True(anchor.X <= cellLeftPx, "left of the cell");
                break;
        }
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Card_ClampsBothAxes_OnEveryEdge(DockEdge edge)
    {
        var at = DockGeometry.Compute(Screen1080, edge, 3, expanded: true, renderScale: 1.0);
        var area = Screen1080.WorkingArea;
        // A card far wider than any anchor math would naturally produce, anchored at the work corner.
        var placed = DockGeometry.ClampCard(edge, 400, 300, new PixelPoint(area.X, area.Y), area);

        Assert.True(placed.X >= area.X + 8 && placed.X + 400 <= area.X + area.Width + 8);
        Assert.True(placed.Y >= area.Y + 8 && placed.Y + 300 <= area.Y + area.Height + 8);
    }

    [Fact]
    public void Card_NegativeOrigin_ClampsIntoThatScreen()
    {
        var left = new ScreenInfo("DP-1", false,
            new PixelBox(-1920, 0, 1920, 1080), new PixelBox(-1920, 0, 1920, 1040), 1.0);
        var at = DockGeometry.Compute(left, DockEdge.Top, 3, expanded: true, renderScale: 1.0);

        Assert.True(at.Placement.X < 0, "window itself lives at negative desktop coordinates");
        var anchor = DockGeometry.CardAnchor(at, DockGeometry.CellRect(at, 0), 1.0);
        var placed = DockGeometry.ClampCard(DockEdge.Top, 280, 200, anchor, left.WorkingArea);
        Assert.True(placed.X >= -1920 + 8 && placed.X + 280 <= 0 + 8);
    }

    [Fact]
    public void Card_SmallerWorkAreaThanCard_StillPlaces()
    {
        var tiny = new PixelBox(0, 0, 100, 100);
        var placed = DockGeometry.ClampCard(DockEdge.Top, 400, 300, new PixelPoint(50, 50), tiny);

        Assert.Equal(8, placed.X);
        Assert.Equal(8, placed.Y);
    }

    [Fact]
    public void Horizontal_WrapsWhenWorkAreaTooNarrow()
    {
        var narrow = new ScreenInfo("eDP-1", true,
            new PixelBox(0, 0, 400, 800), new PixelBox(0, 0, 400, 800), 1.0);
        var at = DockGeometry.Compute(narrow, DockEdge.Top, 9, expanded: true, renderScale: 1.0);

        Assert.True(at.Grid.Secondary > 1, "nine cells wrap into rows on a 400px strip");
        for (var i = 0; i < 9; i++)
            Assert.Equal(i, DockGeometry.CellAt(at, DockGeometry.CellRect(at, i).Center));
        Assert.True(at.Placement.Width <= 400, "wrapped capsule fits the strip");
    }

    [Fact]
    public void Vertical_WrapsIntoColumnsWhenShort()
    {
        var shortScreen = new ScreenInfo("eDP-1", true,
            new PixelBox(0, 0, 800, 700), new PixelBox(0, 0, 800, 700), 1.0);
        var at = DockGeometry.Compute(shortScreen, DockEdge.Left, 9, expanded: true, renderScale: 1.0);

        Assert.True(at.Grid.Secondary > 1, "nine cells wrap into columns on a 700px edge");
        for (var i = 0; i < 9; i++)
            Assert.Equal(i, DockGeometry.CellAt(at, DockGeometry.CellRect(at, i).Center));
        Assert.True(at.Placement.Height <= 700, "wrapped column fits the edge");
    }

    [Fact]
    public void Vertical_NineWrapsLikeWindowsOn1080p()
    {
        // Padding and the slanted ends leave room for eight cells along this edge.
        var at = DockGeometry.Compute(Screen1080, DockEdge.Left, 9, expanded: true, renderScale: 1.0);

        Assert.Equal(8, at.Grid.PerPrimary);
        Assert.Equal(2, at.Grid.Secondary);
        for (var i = 0; i < 9; i++)
            Assert.Equal(i, DockGeometry.CellAt(at, DockGeometry.CellRect(at, i).Center));
        Assert.True(at.Placement.Height <= 1040);
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void ZeroProviders_PlacesSaneCapsule(DockEdge edge)
    {
        var at = DockGeometry.Compute(Screen1080, edge, 0, expanded: true, renderScale: 1.0);

        Assert.True(at.CapsuleAlong > 0 && at.CapsuleAcross > 0);
        Assert.True(at.WindowWidth > 0 && at.WindowHeight > 0);
        Assert.Equal(-1, DockGeometry.CellAt(at, new Point(at.CapsuleLeft + 1, at.CapsuleTop + 1)));
        Assert.Equal(default, DockGeometry.CellRect(at, 0));
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void NineProviders_PlaceOnEveryEdge(DockEdge edge)
    {
        var at = DockGeometry.Compute(Screen1080, edge, 9, expanded: true, renderScale: 1.0);

        Assert.True(at.Placement.Width >= 1 && at.Placement.Height >= 1);
        Assert.Equal(8, DockGeometry.CellAt(at, DockGeometry.CellRect(at, 8).Center));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void Scales_KeepDipSize_ScalePixelPlacement(double scale)
    {
        var screen = Screen1080 with { Scaling = scale };
        var at = DockGeometry.Compute(screen, DockEdge.Top, 3, expanded: false, renderScale: scale);
        var plain = DockGeometry.Compute(Screen1080, DockEdge.Top, 3, expanded: false, renderScale: 1.0);

        Assert.Equal(plain.WindowWidth, at.WindowWidth);
        Assert.Equal(plain.WindowHeight, at.WindowHeight);
        Assert.Equal((int)Math.Round(at.WindowWidth * scale), at.Placement.Width);
        Assert.Equal((int)Math.Round(at.WindowHeight * scale), at.Placement.Height);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void ActualRenderScaleDecidesFitWhenMonitorScaleDiffers(double scale)
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Left, 9, expanded: true, renderScale: scale);
        Assert.True(at.Placement.Height <= Screen1080.WorkingArea.Height);
        Assert.Equal((int)Math.Round(at.WindowWidth * scale), at.Placement.Width);
    }

    [Fact]
    public void Topology_MissingDisplayFallsBack_Primary()
    {
        var screens = new[]
        {
            new ScreenInfo("HDMI-1", true, new PixelBox(0, 0, 1920, 1080), new PixelBox(0, 40, 1920, 1000), 1.0),
            new ScreenInfo("DP-1", false, new PixelBox(1920, 0, 1920, 1080), new PixelBox(1920, 0, 1920, 1080), 1.0)
        };

        var chosen = Displays.Choose(screens, "UNPLUGGED");
        Assert.Equal("HDMI-1", chosen?.Name);

        var home = Displays.Choose(screens, "DP-1");
        Assert.Equal("DP-1", home?.Name);
        var at = DockGeometry.Compute(home, DockEdge.Top, 3, expanded: false, renderScale: 1.0);
        Assert.True(at.Placement.X >= 1920, "dock follows the chosen screen, not the primary one");
    }

    [Fact]
    public void Missing_SignalsAbsentSavedDisplay()
    {
        var screens = new[] { Screen1080 };
        Assert.True(Displays.Missing(screens, "UNPLUGGED"));
        Assert.False(Displays.Missing(screens, "HDMI-1"));
        Assert.False(Displays.Missing(screens, null));
    }

    [Fact]
    public void InputShape_MatchesCapsuleInPixels()
    {
        var at = DockGeometry.Compute(Screen1080, DockEdge.Left, 3, expanded: true, renderScale: 2.0);

        Assert.Equal((int)Math.Round(at.CapsuleLeft * 2.0), at.Input.X);
        Assert.Equal((int)Math.Round(at.CapsuleTop * 2.0), at.Input.Y);
        Assert.Equal(Math.Max(1, (int)Math.Round(at.CapsuleWidth * 2.0)), at.Input.Width);
        Assert.Equal(Math.Max(1, (int)Math.Round(at.CapsuleHeight * 2.0)), at.Input.Height);
    }

    /// <summary>Avalonia 12.1.2 X11Window.UpdateScaling: lowest scale among monitors containing the top-left, edges inclusive.</summary>
    private static double AvaloniaScaleAt(PixelBox window, IEnumerable<ScreenInfo> screens) =>
        screens.OrderBy(s => s.Scaling).First(s => window.X >= s.Bounds.Left && window.X <= s.Bounds.Right
            && window.Y >= s.Bounds.Top && window.Y <= s.Bounds.Bottom).Scaling;

    [Theory]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void LeftDock_RightOfLowerScaleMonitor_RendersAtItsOwnScale(double scale)
    {
        var laptop = new ScreenInfo("eDP-1", true, new PixelBox(0, 0, 1920, 1080), new PixelBox(0, 0, 1920, 1040), 1.0);
        var external = new ScreenInfo("DP-1", false, new PixelBox(1920, 0, 2560, 1440), new PixelBox(1920, 0, 2560, 1440), scale);
        ScreenInfo[] screens = [laptop, external];

        var naive = DockGeometry.Compute(external, DockEdge.Left, 3, expanded: false, renderScale: scale);
        Assert.Equal(1.0, AvaloniaScaleAt(naive.Placement, screens));

        var at = DockGeometry.Compute(external, DockEdge.Left, 3, expanded: false, renderScale: scale, screens);
        Assert.Equal(1921, at.Placement.X);
        Assert.Equal(scale, AvaloniaScaleAt(at.Placement, screens));
    }

    [Fact]
    public void TopDock_BelowLowerScaleMonitor_ClearsTheSharedEdge()
    {
        var upper = new ScreenInfo("DP-1", true, new PixelBox(0, 0, 1920, 1080), new PixelBox(0, 0, 1920, 1080), 1.0);
        var lower = new ScreenInfo("eDP-1", false, new PixelBox(0, 1080, 1920, 1080), new PixelBox(0, 1080, 1920, 1080), 1.5);
        ScreenInfo[] screens = [upper, lower];

        var at = DockGeometry.Compute(lower, DockEdge.Top, 3, expanded: false, renderScale: 1.5, screens);

        Assert.Equal(1081, at.Placement.Y);
        Assert.Equal(1.5, AvaloniaScaleAt(at.Placement, screens));
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void SharedEdgeWithSameOrHigherScale_StaysFlush(double neighbour, double target)
    {
        var left = new ScreenInfo("eDP-1", true, new PixelBox(0, 0, 1920, 1080), new PixelBox(0, 0, 1920, 1080), neighbour);
        var right = new ScreenInfo("DP-1", false, new PixelBox(1920, 0, 1920, 1080), new PixelBox(1920, 0, 1920, 1080), target);

        var at = DockGeometry.Compute(right, DockEdge.Left, 3, expanded: false, renderScale: target, [left, right]);

        Assert.Equal(1920, at.Placement.X);
    }

    [Fact]
    public void NullScreen_FallsBackTo1080p()
    {
        var at = DockGeometry.Compute(null, DockEdge.Top, 3, expanded: false, renderScale: 1.0);

        Assert.Equal((1920 - at.Placement.Width) / 2, at.Placement.X);
        Assert.Equal(0, at.Placement.Y);
    }
}
