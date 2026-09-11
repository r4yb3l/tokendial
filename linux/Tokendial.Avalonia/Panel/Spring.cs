using Avalonia.Animation.Easings;

namespace Tokendial.Linux.Panel;

/// <summary>
/// A damped spring, solved rather than approximated by a bezier. The same solver as
/// windows/Tokendial.App/Panel/Theme.cs's SpringCurve and the Swift one on macOS: three copies of fifteen
/// lines, kept identical because a difference here is invisible in review and obvious on screen.
/// </summary>
public sealed class Spring(double response, double damping) : Easing
{
    private readonly double omega = 2 * Math.PI / response;
    private readonly double zeta = damping;

    public TimeSpan SettleTime { get; } = TimeSpan.FromSeconds(Math.Log(1000) / (damping * (2 * Math.PI / response)));

    public override double Ease(double progress)
    {
        var t = progress * SettleTime.TotalSeconds;
        var decay = Math.Exp(-zeta * omega * t);
        if (zeta >= 1) return 1 - decay * (1 + omega * t);
        var damped = omega * Math.Sqrt(1 - zeta * zeta);
        return 1 - decay * (Math.Cos(damped * t) + zeta * omega / damped * Math.Sin(damped * t));
    }

    /// <summary>The capsule opening and closing.</summary>
    public static readonly Spring Expand = new(0.38, 0.80);

    /// <summary>The contents fading in behind it.</summary>
    public static readonly Spring Contents = new(0.32, 0.84);
}
