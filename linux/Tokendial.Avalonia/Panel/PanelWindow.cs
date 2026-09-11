using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Store;
using Tokendial.Linux.Interop;

// Avalonia's StyledElement already has a Theme property, which shadows the token class inside any
// control. The class keeps the name it has on Windows and macOS - the three are meant to read alike -
// and the alias is what lets it be reached from inside a control.
using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The dock: a trapezoid hanging from the top edge with one dial per provider. Every window-manager
/// concession it needs was measured on Cinnamon before this was written - see tasks/lessons.md - so the
/// X11 calls here are the ones that were proven, not the ones that looked plausible.
/// </summary>
public sealed class PanelWindow : Window
{
    private readonly Canvas root = new();
    // Fully qualified: implicit usings bring System.IO.Path into scope alongside the shape.
    private readonly Avalonia.Controls.Shapes.Path dockFill = new();
    private readonly Avalonia.Controls.Shapes.Path dockEdge = new();
    private readonly StackPanel row = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = Tokens.CompactSpacing };
    private readonly ReadingArchive archive = new();
    private readonly List<Dial> dials = [];

    private double along;

    public PanelWindow()
    {
        Title = "Tokendial";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        dockFill.Fill = Tokens.Surface;
        dockEdge.Stroke = Tokens.SurfaceEdge;
        dockEdge.StrokeThickness = 1;
        row.Margin = new Thickness(0);

        root.Children.Add(dockFill);
        root.Children.Add(dockEdge);
        root.Children.Add(row);
        Content = root;

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var providers = ProviderCatalog.Providers(archive);
        Build(providers.Count);

        var handle = TryGetPlatformHandle();
        if (handle is not null && X11.Open())
        {
            X11.MakeDock(handle.Handle);
            X11.RefuseFocus(handle.Handle);
            Place();
            // Only the trapezoid takes clicks; the hot-zone strip below it stays outside the input region so
            // it can be polled for hover while a click there reaches whatever is behind.
            X11.InputShape(handle.Handle, new X11.XRectangle
            {
                X = 0, Y = 0,
                Width = (ushort)Math.Round(along * (Screens.Primary?.Scaling ?? 1)),
                Height = (ushort)Math.Round(Tokens.CompactHeight * (Screens.Primary?.Scaling ?? 1))
            });
        }

        await Read(providers);
    }

    private void Build(int count)
    {
        along = 2 * Tokens.CompactPadding + count * Tokens.CompactDial + (count - 1) * Tokens.CompactSpacing + 2 * Tokens.DockSlant;
        Width = along;
        Height = Tokens.CompactHeight + Tokens.HotZone;

        dockFill.Data = DockShape.Fill(along, Tokens.CompactHeight, Tokens.DockSlant, Tokens.CompactRadius);
        dockEdge.Data = DockShape.Edge(along, Tokens.CompactHeight, Tokens.DockSlant, Tokens.CompactRadius);

        for (var i = 0; i < count; i++)
        {
            var dial = new Dial(Tokens.CompactDial, Tokens.CompactStroke) { Hollow = true };
            dials.Add(dial);
            row.Children.Add(dial);
        }
        Canvas.SetLeft(row, Tokens.DockSlant + Tokens.CompactPadding);
        Canvas.SetTop(row, (Tokens.CompactHeight - Tokens.CompactDial) / 2);
    }

    /// <summary>Centred on the top edge of the work area, so the panel below is never covered.</summary>
    private void Place()
    {
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = Screens.Primary?.Scaling ?? 1.0;
        Position = new PixelPoint(area.X + (area.Width - (int)Math.Round(along * scale)) / 2, area.Y);
    }

    /// <summary>
    /// Reads every provider once. Off the UI thread by virtue of being async; each result is applied as it
    /// arrives so a slow provider never holds up the ones that answered.
    /// </summary>
    private async Task Read(IReadOnlyList<IUsageProvider> providers)
    {
        for (var i = 0; i < providers.Count; i++)
        {
            var index = i;
            try
            {
                var reading = await providers[index].ReadAsync();
                await Dispatcher.UIThread.InvokeAsync(() => Apply(index, reading));
                Console.WriteLine($"{providers[index].Id,-12} {reading.Status,-24} {reading.HeadlineText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{providers[index].Id,-12} failed: {ex.GetType().Name}");
            }
        }
    }

    private void Apply(int index, ProviderReading reading)
    {
        var dial = dials[index];
        if (reading.HeadlineFraction is double fraction)
        {
            dial.Hollow = false;
            dial.Fill = Tokens.Of(reading.Band);
            dial.Fraction = fraction;
        }
        else dial.Hollow = true;
    }
}
