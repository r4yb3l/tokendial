using Avalonia;
using Avalonia.Media;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The dock hanging from a screen edge: a trapezoid whose base spans the full length along the edge, whose
/// sides slant inward, and whose far corners are rounded. The base has no outline, so the shape reads as
/// part of the edge rather than a floating pill. Ported from windows/Tokendial.App/Panel/DockShape.cs; only
/// the top edge is built here, which is the one the first Linux dock hangs from.
/// </summary>
public static class DockShape
{
    /// <summary>Closed fill.</summary>
    public static Geometry Fill(double along, double across, double slant, double radius) =>
        Build(along, across, slant, radius, closed: true);

    /// <summary>Open hairline along the sides and the far side only; the base stays open against the edge.</summary>
    public static Geometry Edge(double along, double across, double slant, double radius) =>
        Build(along, across, slant, radius, closed: false);

    private static Geometry Build(double along, double across, double slant, double radius, bool closed)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(across / 2, (along - 2 * slant) / 2)));
        var length = Math.Sqrt(slant * slant + across * across);
        var ux = slant / length;
        var uy = across / length;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (closed)
            {
                ctx.BeginFigure(new Point(0, 0), true);
                ctx.LineTo(new Point(along, 0));
            }
            else ctx.BeginFigure(new Point(along, 0), false);

            var farRight = new Point(along - slant, across);
            ctx.LineTo(new Point(farRight.X + ux * r, farRight.Y - uy * r));
            ctx.QuadraticBezierTo(farRight, new Point(farRight.X - r, across));
            ctx.LineTo(new Point(slant + r, across));
            var farLeft = new Point(slant, across);
            ctx.QuadraticBezierTo(farLeft, new Point(farLeft.X - ux * r, farLeft.Y - uy * r));
            ctx.LineTo(new Point(0, 0));
            ctx.EndFigure(closed);
        }
        return geometry;
    }
}
