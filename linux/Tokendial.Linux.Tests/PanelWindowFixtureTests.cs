using Avalonia.Headless.XUnit;
using Tokendial.Core.Settings;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tests;

/// <summary>
/// The actual window, headless: no desktop touched, no mouse moved, no X11 calls. Headless cannot show
/// a real X11 dock (no input shape, no server-verified origin, no compositor), so these prove the wiring
/// the supervisor owns settings for - edge selection reaching the layout - while <see cref="DockGeometryTests"/>
/// prove the numbers. Pointer, shape and stacking stay a hardware check.
/// </summary>
public sealed class PanelWindowFixtureTests
{
    private static readonly string[] Ids = ["claude", "codex", "opencode"];

    [AvaloniaFact]
    public void CtorEdge_ReachesLayout()
    {
        var window = new PanelWindow(Ids, null, DockEdge.Left);

        Assert.Equal(DockEdge.Left, window.Edge);
        window.Close();
    }

    [AvaloniaFact]
    public void SetEdge_RewiresLayout_OnRealWindow()
    {
        var window = new PanelWindow(Ids);

        Assert.Equal(DockEdge.Top, window.Edge);
        window.SetEdge(DockEdge.Right);
        Assert.Equal(DockEdge.Right, window.Edge);
        Assert.NotNull(window.LastLayout);
        Assert.Equal(DockEdge.Right, window.LastLayout!.Edge);
        Assert.True(window.LastLayout.Vertical);

        window.SetEdge(DockEdge.Bottom);
        Assert.Equal(DockEdge.Bottom, window.LastLayout!.Edge);
        Assert.False(window.LastLayout.Vertical);
        window.Close();
    }

    [AvaloniaFact]
    public void SetEdge_SameEdge_IsNoOp()
    {
        var window = new PanelWindow(Ids, null, DockEdge.Top);
        window.SetEdge(DockEdge.Top);

        Assert.Equal(DockEdge.Top, window.Edge);
        window.Close();
    }

    [AvaloniaFact]
    public void ShownWindow_PlacesAndCloses_Cleanly()
    {
        var window = new PanelWindow(Ids, null, DockEdge.Left);
        window.Show();

        Assert.NotNull(window.LastLayout);
        Assert.True(window.Width > 0 && window.Height > 0);

        window.SetEdge(DockEdge.Bottom);
        Assert.Equal(DockEdge.Bottom, window.LastLayout!.Edge);

        window.Close();
    }
}
