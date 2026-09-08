using System.Windows;
using System.Windows.Media;

namespace Tokendial.App.Panel;

/// <summary>
/// The dock hanging from the screen edge: a trapezoid whose top spans the full width,
/// whose sides slant inward, and whose bottom corners are rounded. The top edge has no
/// outline, so the shape reads as part of the edge rather than a floating pill.
/// </summary>
public static class DockShape
{
    /// <summary>Closed fill: (0,0) → (w,0) → slanted side → rounded bottom → slanted side back.</summary>
    public static Geometry Fill(double width, double height, double slant, double radius)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(width, 0), false, false);
            Side(ctx, width, height, slant, radius, rightSide: true);
            Side(ctx, width, height, slant, radius, rightSide: false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Open hairline along the sides and bottom only; the top stays open against the edge.</summary>
    public static Geometry Edge(double width, double height, double slant, double radius)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(width, 0), false, false);
            Side(ctx, width, height, slant, radius, rightSide: true);
            Side(ctx, width, height, slant, radius, rightSide: false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>From the top corner down the slanted side into the rounded bottom corner, then along the bottom to the other corner.</summary>
    private static void Side(StreamGeometryContext ctx, double width, double height, double slant, double radius, bool rightSide)
    {
        var r = Math.Min(radius, Math.Min(height / 2, (width - 2 * slant) / 2));
        var length = Math.Sqrt(slant * slant + height * height);
        var ux = slant / length;
        var uy = height / length;
        if (rightSide)
        {
            var corner = new Point(width - slant, height);
            ctx.LineTo(new Point(corner.X + ux * r, corner.Y - uy * r), true, true);
            ctx.QuadraticBezierTo(corner, new Point(corner.X - r, height), true, true);
            ctx.LineTo(new Point(slant + r, height), true, true);
        }
        else
        {
            var corner = new Point(slant, height);
            ctx.QuadraticBezierTo(corner, new Point(corner.X - ux * r, corner.Y - uy * r), true, true);
            ctx.LineTo(new Point(0, 0), true, true);
        }
    }
}
