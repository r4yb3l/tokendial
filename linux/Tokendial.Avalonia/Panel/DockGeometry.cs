using Avalonia;
using Tokendial.Core.Model;
using Tokendial.Core.Settings;
using Tokendial.Linux.Interop;

namespace Tokendial.Linux.Panel;

/// <summary>
/// Every number the four-edge dock needs, computed from a screen, an edge, a provider count and a scale -
/// and nothing else. The panel window calls this for placement, capsule rectangles, hot-zone hit testing
/// and card anchors; the tests call it directly. Pure: no windows, no X11, no pointer.
/// </summary>
/// <remarks>Layout dimensions are DIPs; placement and input rectangles are physical pixels. The actual
/// window render scale controls both fitting and conversion, including during monitor transitions.</remarks>
public static class DockGeometry
{
    public const double CardGap = 6;
    public const double CardMargin = 8;

    /// <summary>How the expanded cells fill the capsule: per row (horizontal) or per column (vertical).</summary>
    /// <param name="PerPrimary">Cells in each row down a side's column, or in the row along the top/bottom.</param>
    /// <param name="Secondary">Rows, or columns down a side. 1 is the common case: no wrapping.</param>
    /// <param name="Along">Content length along the edge, before the capsule adds its slant at each end.</param>
    /// <param name="Across">Content depth away from the edge, before the hot zone.</param>
    public sealed record CellGrid(int PerPrimary, int Secondary, double Along, double Across);

    /// <summary>One placement of the dock window, for the capsule size the dock currently shows.</summary>
    public sealed record Layout(
        DockEdge Edge,
        bool Vertical,
        int Count,
        double WindowWidth,
        double WindowHeight,
        double CapsuleAlong,
        double CapsuleAcross,
        double CapsuleLeft,
        double CapsuleTop,
        PixelBox Placement,
        X11.XRectangle Input,
        Rect Hot,
        CellGrid Grid,
        double ExpandedAlong,
        double ExpandedAcross,
        double CompactAlong)
    {
        public double CapsuleWidth => Vertical ? CapsuleAcross : CapsuleAlong;
        public double CapsuleHeight => Vertical ? CapsuleAlong : CapsuleAcross;
    };

    /// <summary>Fallback when no monitor answers: a single 1080p screen at the origin.</summary>
    public static readonly ScreenInfo FallbackScreen = new("", true,
        new PixelBox(0, 0, 1920, 1080), new PixelBox(0, 0, 1920, 1080), 1.0);

    /// <summary>The compact capsule's length along the edge, slant included.</summary>
    public static double CompactAlong(int count)
    {
        var content = count <= 0
            ? 2 * Theme.CompactPadding + 40
            : 2 * Theme.CompactPadding + count * Theme.CompactDial + (count - 1) * Theme.CompactSpacing;
        return content + 2 * Theme.DockSlant;
    }

    /// <summary>
    /// The expanded cells' box for a count along an edge with this much room (DIPs). A row along the
    /// top or bottom is one row unless it would run off the work area, and then it wraps into as many
    /// rows as it takes; a column down a side wraps into columns the way the Windows dock does. Cells
    /// stay upright either way - nothing is rotated, only re-flowed.
    /// </summary>
    public static CellGrid ExpandedGrid(int count, bool vertical, double available)
    {
        var cells = Math.Max(count, 1);
        if (!vertical)
        {
            var single = Math.Max(2 * Theme.ExpandedPadding + cells * Theme.CellWidth,
                Theme.CardWidth + 2 * Theme.ExpandedPadding);
            if (single + 2 * Theme.DockSlant <= available)
                return new CellGrid(cells, 1, single, Theme.ExpandedHeight);
            var perRow = Math.Max(1, (int)((available - 2 * Theme.DockSlant - 2 * Theme.ExpandedPadding)
                / Theme.CellWidth));
            var rows = (int)Math.Ceiling((double)cells / perRow);
            return new CellGrid(perRow, rows,
                2 * Theme.ExpandedPadding + perRow * Theme.CellWidth,
                2 * Theme.ExpandedPadding + rows * Theme.CellHeight + (rows - 1) * Theme.CellGap);
        }
        var ends = 2 * Theme.ExpandedPadding;
        var room = Math.Max(Theme.CellHeight, available - 2 * Theme.DockSlant - ends);
        var fit = Math.Max(1, (int)((room + Theme.CellGap) / (Theme.CellHeight + Theme.CellGap)));
        var perColumn = Math.Min(cells, fit);
        var columns = (int)Math.Ceiling((double)cells / perColumn);
        return new CellGrid(perColumn, columns,
            ends + perColumn * Theme.CellHeight + (perColumn - 1) * Theme.CellGap,
            columns * Theme.CellWidth + 2 * Theme.ExpandedPadding);
    }

    /// <summary>
    /// Places the window and everything in it. `renderScale` is the window's actual RenderScaling;
    /// that same scale decides the fit. A zero or negative render scale falls back to the
    /// target scale, so headless windows without a platform still place.
    /// </summary>
    public static Layout Compute(ScreenInfo? screen, DockEdge edge, int count, bool expanded, double renderScale,
        IReadOnlyList<ScreenInfo>? screens = null)
    {
        var vertical = DockPlacement.IsVertical(edge);
        var target = screen ?? FallbackScreen;
        var fitScale = target.Scaling > 0 ? target.Scaling : 1.0;
        var px = renderScale > 0 ? renderScale : fitScale;
        var work = target.WorkingArea;

        var grid = ExpandedGrid(count, vertical,
            (vertical ? work.Height : work.Width) / px);

        var expandedAlong = grid.Along + 2 * Theme.DockSlant;
        var expandedAcross = grid.Across;
        var compactAlong = CompactAlong(count);
        const double compactAcross = Theme.CompactHeight;

        var capsuleAlong = expanded ? expandedAlong : compactAlong;
        var capsuleAcross = expanded ? expandedAcross : compactAcross;
        var windowAlong = Math.Max(expandedAlong, compactAlong);
        var windowAcross = Math.Max(expandedAcross, compactAcross) + Theme.HotZone;
        var windowWidth = vertical ? windowAcross : windowAlong;
        var windowHeight = vertical ? windowAlong : windowAcross;

        var box = ClearOfLowerScaleNeighbour(DockPlacement.For(target, edge,
            Math.Max(1, (int)Math.Round(windowWidth * px)),
            Math.Max(1, (int)Math.Round(windowHeight * px))), target, screens);

        var capsuleWidth = vertical ? capsuleAcross : capsuleAlong;
        var capsuleHeight = vertical ? capsuleAlong : capsuleAcross;
        var (capsuleLeft, capsuleTop) = edge switch
        {
            DockEdge.Bottom => ((windowWidth - capsuleWidth) / 2, windowHeight - capsuleHeight),
            DockEdge.Left => (0.0, (windowHeight - capsuleHeight) / 2),
            DockEdge.Right => (windowWidth - capsuleWidth, (windowHeight - capsuleHeight) / 2),
            _ => ((windowWidth - capsuleWidth) / 2, 0.0)
        };

        var input = new X11.XRectangle
        {
            X = (short)Math.Round(capsuleLeft * px),
            Y = (short)Math.Round(capsuleTop * px),
            Width = (ushort)Math.Max(1, Math.Round(capsuleWidth * px)),
            Height = (ushort)Math.Max(1, Math.Round(capsuleHeight * px))
        };

        var hot = edge switch
        {
            DockEdge.Bottom => new Rect(capsuleLeft, capsuleTop - Theme.HotZone, capsuleWidth, capsuleHeight + Theme.HotZone),
            DockEdge.Left => new Rect(capsuleLeft, capsuleTop, capsuleWidth + Theme.HotZone, capsuleHeight),
            DockEdge.Right => new Rect(capsuleLeft - Theme.HotZone, capsuleTop, capsuleWidth + Theme.HotZone, capsuleHeight),
            _ => new Rect(capsuleLeft, capsuleTop, capsuleWidth, capsuleHeight + Theme.HotZone)
        };

        return new Layout(edge, vertical, Math.Max(count, 0), windowWidth, windowHeight,
            capsuleAlong, capsuleAcross, capsuleLeft, capsuleTop, box, input, hot, grid,
            expandedAlong, expandedAcross, compactAlong);
    }

    /// <summary>
    /// Moves the window one pixel off a monitor boundary it shares with a lower-scale neighbour.
    /// </summary>
    /// <remarks>
    /// Avalonia 12's X11 window takes its render scale from the lowest-scale monitor whose bounds contain the
    /// window's top-left corner, and <c>PixelRect.Contains</c> counts the right and bottom edges as inside. A
    /// left dock on a monitor to the right of a lower-scale one starts exactly on that shared edge, so without
    /// this it renders at the neighbour's scale: a 1x dock on a 1.25x or 2x panel. Seen on a nested X server
    /// with monitors at 1 and 1.25; one pixel is the smallest move that leaves the ambiguous point.
    /// </remarks>
    public static PixelBox ClearOfLowerScaleNeighbour(PixelBox box, ScreenInfo target, IReadOnlyList<ScreenInfo>? screens)
    {
        if (screens is null) return box;
        foreach (var other in screens)
        {
            if (other.Name == target.Name || other.Scaling >= target.Scaling) continue;
            var bounds = other.Bounds;
            var claimed = box.X >= bounds.Left && box.X <= bounds.Right && box.Y >= bounds.Top && box.Y <= bounds.Bottom;
            if (!claimed) continue;
            if (box.X == bounds.Right) box = box with { X = box.X + 1 };
            if (box.Y == bounds.Bottom) box = box with { Y = box.Y + 1 };
        }
        return box;
    }

    /// <summary>Where the expanded cells start inside the capsule, in capsule-relative DIPs.</summary>
    public static Point GridOrigin(Layout layout)
    {
        if (!layout.Vertical)
        {
            var contentAcross = layout.Grid.Secondary * Theme.CellHeight + (layout.Grid.Secondary - 1) * Theme.CellGap;
            return new Point(
                (layout.CapsuleAlong - layout.Grid.PerPrimary * Theme.CellWidth) / 2,
                (layout.CapsuleAcross - contentAcross) / 2);
        }
        return new Point(
            (layout.CapsuleAcross - layout.Grid.Secondary * Theme.CellWidth) / 2,
            Theme.DockSlant + Theme.ExpandedPadding);
    }

    /// <summary>Which cell a window-relative DIP point falls in, or -1. The hot zone beyond the cells is not a cell.</summary>
    public static int CellAt(Layout layout, Point local)
    {
        var origin = GridOrigin(layout);
        // Along the edge first: u runs down a side's column or along a row, v runs away from the edge.
        var u = (layout.Vertical ? local.Y - layout.CapsuleTop : local.X - layout.CapsuleLeft) - (layout.Vertical ? origin.Y : origin.X);
        var v = (layout.Vertical ? local.X - layout.CapsuleLeft : local.Y - layout.CapsuleTop) - (layout.Vertical ? origin.X : origin.Y);
        if (u < 0 || v < 0) return -1;
        int primary, secondary;
        if (!layout.Vertical)
        {
            primary = (int)Math.Floor(u / Theme.CellWidth);
            secondary = (int)Math.Floor(v / (Theme.CellHeight + Theme.CellGap));
            if (primary < 0 || primary >= layout.Grid.PerPrimary || secondary < 0 || secondary >= layout.Grid.Secondary)
                return -1;
            var index = secondary * layout.Grid.PerPrimary + primary;
            return index < layout.Count && CellRect(layout, index).Contains(local) ? index : -1;
        }
        primary = (int)Math.Floor(u / (Theme.CellHeight + Theme.CellGap));
        secondary = (int)Math.Floor(v / Theme.CellWidth);
        if (primary < 0 || primary >= layout.Grid.PerPrimary || secondary < 0 || secondary >= layout.Grid.Secondary)
            return -1;
        var down = secondary * layout.Grid.PerPrimary + primary;
        return down < layout.Count && CellRect(layout, down).Contains(local) ? down : -1;
    }

    /// <summary>The cell's rectangle in window-relative DIPs. Empty when the index is not a cell.</summary>
    public static Rect CellRect(Layout layout, int index)
    {
        if (index < 0 || index >= layout.Count) return default;
        var origin = GridOrigin(layout);
        if (!layout.Vertical)
        {
            var row = index / layout.Grid.PerPrimary;
            var col = index % layout.Grid.PerPrimary;
            return new Rect(
                layout.CapsuleLeft + origin.X + col * Theme.CellWidth,
                layout.CapsuleTop + origin.Y + row * (Theme.CellHeight + Theme.CellGap),
                Theme.CellWidth, Theme.CellHeight);
        }
        var column = index / layout.Grid.PerPrimary;
        var down = index % layout.Grid.PerPrimary;
        return new Rect(
            layout.CapsuleLeft + origin.X + column * Theme.CellWidth,
            layout.CapsuleTop + origin.Y + down * (Theme.CellHeight + Theme.CellGap),
            Theme.CellWidth, Theme.CellHeight);
    }

    /// <summary>
    /// The point the card hangs off, in desktop pixels: just inside the screen from the capsule, centred
    /// on the cell. The card opens toward the interior on every edge - below a top dock, above a bottom
    /// one, beside a column.
    /// </summary>
    public static PixelPoint CardAnchor(Layout layout, Rect cell, double renderScale)
    {
        var px = renderScale > 0 ? renderScale : 1.0;
        var centreX = layout.Placement.X + (int)Math.Round((cell.X + cell.Width / 2) * px);
        var centreY = layout.Placement.Y + (int)Math.Round((cell.Y + cell.Height / 2) * px);
        return layout.Edge switch
        {
            DockEdge.Bottom => new PixelPoint(centreX,
                layout.Placement.Y + (int)Math.Round((layout.CapsuleTop - CardGap) * px)),
            DockEdge.Left => new PixelPoint(
                layout.Placement.X + (int)Math.Round((layout.CapsuleLeft + layout.CapsuleWidth + CardGap) * px),
                centreY),
            DockEdge.Right => new PixelPoint(
                layout.Placement.X + (int)Math.Round((layout.CapsuleLeft - CardGap) * px),
                centreY),
            _ => new PixelPoint(centreX,
                layout.Placement.Y + (int)Math.Round((layout.CapsuleTop + layout.CapsuleHeight + CardGap) * px))
        };
    }

    /// <summary>
    /// Pins the card inside the work area on both axes, from an anchor on its outer side. A work area
    /// smaller than the card still places it rather than throwing: the low bound never passes the high.
    /// </summary>
    public static PixelPoint ClampCard(DockEdge edge, int cardWidth, int cardHeight, PixelPoint anchor, PixelBox area)
    {
        var x = edge switch
        {
            DockEdge.Left => anchor.X,
            DockEdge.Right => anchor.X - cardWidth,
            _ => anchor.X - cardWidth / 2
        };
        var y = edge switch
        {
            DockEdge.Top => anchor.Y,
            DockEdge.Bottom => anchor.Y - cardHeight,
            _ => anchor.Y - cardHeight / 2
        };
        var lowX = area.X + (int)CardMargin;
        var highX = Math.Max(lowX, area.X + area.Width - cardWidth - (int)CardMargin);
        var lowY = area.Y + (int)CardMargin;
        var highY = Math.Max(lowY, area.Y + area.Height - cardHeight - (int)CardMargin);
        return new PixelPoint(Math.Clamp(x, lowX, highX), Math.Clamp(y, lowY, highY));
    }
}
