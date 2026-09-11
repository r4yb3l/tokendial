using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;
using Tokendial.Linux.Interop;

// Avalonia's StyledElement already has a Theme property, which shadows the token class inside any
// control. The class keeps the name it has on Windows and macOS - the three are meant to read alike -
// and the alias is what lets it be reached from inside a control.
using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The dock: a trapezoid hanging from the top edge with one cell per provider, each a dial with the
/// provider's mark inside it. Every window-manager concession it needs was measured on Cinnamon before this
/// was written - see tasks/lessons.md - so the X11 calls here are the ones that were proven.
/// </summary>
public sealed class PanelWindow : Window
{
    private readonly Canvas root = new();
    // Fully qualified: implicit usings bring System.IO.Path into scope alongside the shape.
    private readonly Avalonia.Controls.Shapes.Path dockFill = new();
    private readonly Avalonia.Controls.Shapes.Path dockEdge = new();
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.CompactSpacing };
    private readonly ReadingArchive archive = new();
    private readonly Settings settings = Settings.Load();
    private readonly CardWindow card = new();

    private readonly List<Dial> dials = [];
    private readonly List<MarkView> marks = [];
    private readonly List<Tile> tiles = [];
    private IReadOnlyList<IUsageProvider> providers = [];

    private DispatcherTimer? poll;
    private double along;
    private int hovered = -1;

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

        root.Children.Add(dockFill);
        root.Children.Add(dockEdge);
        root.Children.Add(row);
        Content = root;

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        providers = Adopt(ProviderCatalog.Providers(archive));
        Build();

        var handle = TryGetPlatformHandle();
        if (handle is not null && X11.Open())
        {
            X11.MakeDock(handle.Handle);
            X11.RefuseFocus(handle.Handle);
            Place();
            var scale = Screens.Primary?.Scaling ?? 1;
            // Only the trapezoid takes clicks. The hot-zone strip below stays outside the input region, so a
            // click there reaches the desktop while the cursor poll still sees the pointer in it.
            X11.InputShape(handle.Handle, new X11.XRectangle
            {
                X = 0, Y = 0,
                Width = (ushort)Math.Round(along * scale),
                Height = (ushort)Math.Round(Tokens.CompactHeight * scale)
            });
        }

        StartPolling();
        await Read();
    }

    /// <summary>
    /// The dock shows the tools you actually use, not the nine that exist. The rule is the one
    /// windows/Tokendial.App/App.cs:124 already applies: a provider met for the first time whose
    /// <c>Account()</c> is null - no credential, so not signed in or not installed - is recorded as
    /// disconnected, and disconnected providers are not shown. Connecting one later is a settings
    /// decision the user makes, never something discovered behind their back.
    /// </summary>
    private IReadOnlyList<IUsageProvider> Adopt(IReadOnlyList<IUsageProvider> all)
    {
        var fresh = all.Where(p => !settings.Known.Contains(p.Id)).ToList();
        if (fresh.Count > 0)
        {
            foreach (var provider in fresh)
            {
                settings.Known.Add(provider.Id);
                if (provider.Account() is null) settings.Disconnected.Add(provider.Id);
            }
            try { settings.Save(); } catch (IOException) { }
        }
        return all.Where(p => !settings.Disconnected.Contains(p.Id)).ToList();
    }

    private void Build()
    {
        var count = providers.Count;
        along = 2 * Tokens.CompactPadding + count * Tokens.CompactDial + (count - 1) * Tokens.CompactSpacing + 2 * Tokens.DockSlant;
        Width = along;
        Height = Tokens.CompactHeight + Tokens.HotZone;

        dockFill.Data = DockShape.Fill(along, Tokens.CompactHeight, Tokens.DockSlant, Tokens.CompactRadius);
        dockEdge.Data = DockShape.Edge(along, Tokens.CompactHeight, Tokens.DockSlant, Tokens.CompactRadius);

        foreach (var provider in providers)
        {
            var dial = new Dial(Tokens.CompactDial, Tokens.CompactStroke) { Hollow = true };
            var mark = new MarkView(provider.Id, Tokens.CompactMark)
            {
                Fill = Tokens.TextDisabled,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dials.Add(dial);
            marks.Add(mark);
            tiles.Add(null!);
            // The mark sits inside the ring, which is why the dial is hollow in the middle.
            row.Children.Add(new Avalonia.Controls.Panel { Width = Tokens.CompactDial, Height = Tokens.CompactDial, Children = { dial, mark } });
        }

        Canvas.SetLeft(row, Tokens.DockSlant + Tokens.CompactPadding);
        Canvas.SetTop(row, (Tokens.CompactHeight - Tokens.CompactDial) / 2);
    }

    /// <summary>Centred on the top edge of the work area, so the desktop panel is never covered.</summary>
    private void Place()
    {
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = Screens.Primary?.Scaling ?? 1.0;
        Position = new PixelPoint(area.X + (area.Width - (int)Math.Round(along * scale)) / 2, area.Y);
    }

    private async Task Read()
    {
        for (var i = 0; i < providers.Count; i++)
        {
            var index = i;
            var provider = providers[index];
            ProviderReading reading;
            try { reading = await provider.ReadAsync(); }
            catch (Exception ex)
            {
                // A provider that cannot be read still has something to say - "not signed in", "nothing
                // metered here", an HTTP code - and the card says it. UsageStore.StatusFor is the one
                // translation from exception to status, and is reused rather than rewritten.
                reading = new ProviderReading(provider.Id, provider.DisplayName, Fidelity.Official,
                    UsageStore.StatusFor(ex), []);
            }
            var account = provider.Account();
            await Dispatcher.UIThread.InvokeAsync(() => Apply(index, reading, account));
            Console.WriteLine($"{provider.Id,-12} {reading.Status,-26} {reading.HeadlineText}");
        }
    }

    private void Apply(int index, ProviderReading reading, ProviderAccount? account)
    {
        tiles[index] = new Tile(reading.ProviderId, reading.DisplayName, Tile.MarkFor(reading.ProviderId),
            reading, account, null, false);

        var dial = dials[index];
        if (reading.HeadlineFraction is double fraction)
        {
            dial.Hollow = false;
            dial.Fill = Tokens.Of(reading.Band);
            dial.Fraction = fraction;
            marks[index].Fill = Tokens.TextPrimary;
        }
        else
        {
            dial.Hollow = true;
            marks[index].Fill = Tokens.TextDisabled;
        }
    }

    /// <summary>
    /// Polled, not evented, for the reason the Windows dock polls: a transparent window whose input region
    /// excludes the hot zone never receives enter or leave for the part that matters most.
    /// </summary>
    private void StartPolling()
    {
        poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        poll.Tick += (_, _) => Track();
        poll.Start();
    }

    private void Track()
    {
        var (x, y, sameScreen) = X11.Pointer();
        if (!sameScreen) return;

        var scale = Screens.Primary?.Scaling ?? 1.0;
        var origin = Position;
        var local = new Point((x - origin.X) / scale, (y - origin.Y) / scale);

        // The strip below the capsule counts as hovering it, so the pointer does not have to land on the
        // ring itself for the card to stay open.
        var within = local.X >= 0 && local.X <= along && local.Y >= 0 && local.Y <= Tokens.CompactHeight + Tokens.HotZone;
        var index = within ? CellAt(local.X) : -1;
        if (index == hovered) return;

        hovered = index;
        if (index < 0 || tiles.Count <= index || tiles[index] is null) { card.HideCard(); return; }

        var left = Tokens.DockSlant + Tokens.CompactPadding + index * (Tokens.CompactDial + Tokens.CompactSpacing);
        var anchor = new PixelPoint(
            origin.X + (int)Math.Round((left + Tokens.CompactDial / 2) * scale),
            origin.Y + (int)Math.Round((Tokens.CompactHeight + 6) * scale));
        card.ShowAt(HoverCard.Build(tiles[index], DateTimeOffset.Now), anchor,
            Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080));
    }

    /// <summary>Which cell a horizontal position falls in, with the gaps counted to the nearer cell.</summary>
    private int CellAt(double x)
    {
        var first = Tokens.DockSlant + Tokens.CompactPadding;
        var pitch = Tokens.CompactDial + Tokens.CompactSpacing;
        var index = (int)Math.Floor((x - first + Tokens.CompactSpacing / 2) / pitch);
        return index >= 0 && index < dials.Count ? index : -1;
    }
}
