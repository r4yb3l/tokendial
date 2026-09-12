using Avalonia;
using Avalonia.Media;
using Tokendial.Core.Settings;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The dock hanging from a screen edge: a trapezoid whose base spans the full length along the edge, whose
/// sides slant inward, and whose far corners are rounded. The base has no outline, so the shape reads as
/// part of the edge rather than a floating pill. The shape is drawn as if the edge were the top, then
/// mapped onto the edge it actually hangs from - the same two-step the Windows twin uses.
/// </summary>
public static class DockShape
{
    /// <summary>Closed fill.</summary>
    public static Geometry Fill(double along, double across, double slant, double radius, DockEdge edge) =>
        Build(along, across, slant, radius, edge, closed: true);

    /// <summary>Open hairline along the sides and the far side only; the base stays open against the edge.</summary>
    public static Geometry Edge(double along, double across, double slant, double radius, DockEdge edge) =>
        Build(along, across, slant, radius, edge, closed: false);

    private static Geometry Build(double along, double across, double slant, double radius, DockEdge edge, bool closed)
    {
        Point Map(double x, double y) => edge switch
        {
            DockEdge.Bottom => new Point(x, across - y),
            DockEdge.Left => new Point(y, x),
            DockEdge.Right => new Point(across - y, x),
            _ => new Point(x, y)
        };
        var r = Math.Max(0, Math.Min(radius, Math.Min(across / 2, (along - 2 * slant) / 2)));
        var length = Math.Sqrt(slant * slant + across * across);
        var ux = slant / length;
        var uy = across / length;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (closed)
            {
                ctx.BeginFigure(Map(0, 0), true);
                ctx.LineTo(Map(along, 0));
            }
            else ctx.BeginFigure(Map(along, 0), false);

            var farRight = (X: along - slant, Y: across);
            ctx.LineTo(Map(farRight.X + ux * r, farRight.Y - uy * r));
            ctx.QuadraticBezierTo(Map(farRight.X, farRight.Y), Map(farRight.X - r, across));
            ctx.LineTo(Map(slant + r, across));
            var farLeft = (X: slant, Y: across);
            ctx.QuadraticBezierTo(Map(farLeft.X, farLeft.Y), Map(farLeft.X - ux * r, farLeft.Y - uy * r));
            ctx.LineTo(Map(0, 0));
            ctx.EndFigure(closed);
        }
        return geometry;
    }
}
