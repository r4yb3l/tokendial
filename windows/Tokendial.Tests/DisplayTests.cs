using Tokendial.Core.Model;
using Tokendial.Core.Settings;

namespace Tokendial.Tests;

/// <summary>
/// Which monitor the dock goes on, and where on it.
/// </summary>
/// <remarks>
/// The placement numbers here were taken from windows/Tokendial.App/Panel/PanelWindow.cs as it shipped in
/// 0.1.3, before that file was changed to call <see cref="DockPlacement"/>. They exist so that a change
/// about monitors cannot move the dock of somebody who only has one.
/// </remarks>
public class DisplayTests
{
    /// <summary>A laptop screen with a 48px taskbar along the bottom, at the desktop origin.</summary>
    private static readonly ScreenInfo Laptop = new("eDP-1", IsPrimary: true,
        Bounds: new PixelBox(0, 0, 1920, 1080),
        WorkingArea: new PixelBox(0, 0, 1920, 1032),
        Scaling: 1.0);

    /// <summary>A second monitor to the right of it, no taskbar, so its work area is its bounds.</summary>
    private static readonly ScreenInfo External = new("HDMI-1", IsPrimary: false,
        Bounds: new PixelBox(1920, 0, 2560, 1440),
        WorkingArea: new PixelBox(1920, 0, 2560, 1440),
        Scaling: 1.0);

    private static readonly ScreenInfo[] Both = [Laptop, External];

    // ---- choosing ------------------------------------------------------------------------------------

    [Fact]
    public void NothingSavedFollowsThePrimaryMonitor() =>
        Assert.Equal(Laptop, Displays.Choose(Both, null));

    [Fact]
    public void AsavedNameIsHonouredWhenThatMonitorIsAttached() =>
        Assert.Equal(External, Displays.Choose(Both, "HDMI-1"));

    /// <summary>
    /// The case the whole by-name design exists for: unplug the chosen monitor and the dock goes to the
    /// primary one rather than off the side of the desktop, and plugging it back in restores it with no
    /// action from the user - because nothing was ever remembered except the name.
    /// </summary>
    [Fact]
    public void AmonitorThatIsNotThereFallsBackAndComesBackByItself()
    {
        ScreenInfo[] alone = [Laptop];
        Assert.Equal(Laptop, Displays.Choose(alone, "HDMI-1"));
        Assert.True(Displays.Missing(alone, "HDMI-1"));

        Assert.Equal(External, Displays.Choose(Both, "HDMI-1"));
        Assert.False(Displays.Missing(Both, "HDMI-1"));
    }

    /// <summary>An XRandR layout with no output flagged primary is real; the first screen serves.</summary>
    [Fact]
    public void WithNoPrimaryTheFirstScreenServes()
    {
        ScreenInfo[] none = [External with { IsPrimary = false }, Laptop with { IsPrimary = false }];
        Assert.Equal("HDMI-1", Displays.Choose(none, null)!.Name);
    }

    [Fact]
    public void NoScreensAtAllIsNotACrash() =>
        Assert.Null(Displays.Choose([], "HDMI-1"));

    [Fact]
    public void AnEmptySavedNameIsTreatedAsNothingSaved() =>
        Assert.Equal(Laptop, Displays.Choose(Both, ""));

    // ---- placing -------------------------------------------------------------------------------------

    /// <summary>
    /// The four edges on the primary screen, with a taskbar along the bottom. Top sits on the very edge
    /// because the work area starts at the same y as the bounds; bottom is lifted clear of the taskbar.
    /// </summary>
    [Theory]
    [InlineData(DockEdge.Top, 560, 0)]
    [InlineData(DockEdge.Bottom, 560, 832)]
    [InlineData(DockEdge.Left, 0, 416)]
    [InlineData(DockEdge.Right, 1120, 416)]
    public void TheFourEdgesOfThePrimaryScreen(DockEdge edge, int x, int y)
    {
        var box = DockPlacement.For(Laptop, edge, 800, 200);
        Assert.Equal(x, box.X);
        Assert.Equal(y, box.Y);
        Assert.Equal(800, box.Width);
        Assert.Equal(200, box.Height);
    }

    /// <summary>
    /// A taskbar along the top pushes the dock below it rather than under it. This is the branch
    /// `work.Top > bounds.Top ? work.Top : bounds.Top` and it is easy to lose in a refactor.
    /// </summary>
    [Fact]
    public void AtaskbarAlongTheTopPushesTheDockBelowIt()
    {
        var topBar = Laptop with { WorkingArea = new PixelBox(0, 48, 1920, 1032) };
        Assert.Equal(48, DockPlacement.For(topBar, DockEdge.Top, 800, 200).Y);
        Assert.Equal(0, DockPlacement.For(Laptop, DockEdge.Top, 800, 200).Y);
    }

    /// <summary>
    /// A horizontal dock centres on the full bounds so a taskbar down one side does not shove the capsule
    /// off centre; a vertical one centres on the work area so it never runs under one. The asymmetry is
    /// deliberate, and this is the test that says so.
    /// </summary>
    [Fact]
    public void AsideTaskbarMovesAvericalDockButNotAHorizontalOne()
    {
        var sideBar = Laptop with { WorkingArea = new PixelBox(72, 0, 1848, 1080) };
        Assert.Equal(560, DockPlacement.For(sideBar, DockEdge.Top, 800, 200).X);
        Assert.Equal(72, DockPlacement.For(sideBar, DockEdge.Left, 200, 800).X);
        Assert.Equal(140, DockPlacement.For(sideBar, DockEdge.Left, 200, 800).Y);
    }

    /// <summary>
    /// The point of the whole change: on a monitor that starts at x=1920 the dock lands there, not back at
    /// the origin. Every coordinate is desktop-wide, which is what both window systems already expect.
    /// </summary>
    [Theory]
    [InlineData(DockEdge.Top, 3100, 0)]
    [InlineData(DockEdge.Bottom, 3100, 1240)]
    [InlineData(DockEdge.Left, 1920, 620)]
    [InlineData(DockEdge.Right, 4280, 620)]
    public void ThesecondMonitorGetsItsOwnCoordinates(DockEdge edge, int x, int y)
    {
        var box = DockPlacement.For(External, edge, 200, 200);
        Assert.Equal(x, box.X);
        Assert.Equal(y, box.Y);
    }

    [Theory]
    [InlineData(DockEdge.Top, false)]
    [InlineData(DockEdge.Bottom, false)]
    [InlineData(DockEdge.Left, true)]
    [InlineData(DockEdge.Right, true)]
    public void OnlyTheSideEdgesAreVertical(DockEdge edge, bool vertical) =>
        Assert.Equal(vertical, DockPlacement.IsVertical(edge));

    [Fact]
    public void TheRoomAlongAnEdgeComesFromTheWorkArea()
    {
        Assert.Equal(1920, DockPlacement.Available(Laptop, DockEdge.Top));
        Assert.Equal(1032, DockPlacement.Available(Laptop, DockEdge.Left));
        Assert.Equal(2560, DockPlacement.Available(External, DockEdge.Bottom));
    }
}
