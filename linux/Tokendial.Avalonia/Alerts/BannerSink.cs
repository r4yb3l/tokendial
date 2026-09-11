using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;
using Tokendial.Linux.Interop;
using Tokendial.Linux.Panel;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Alerts;

/// <summary>
/// Tokendial's own notification: a card in the top-right corner, drawn by Tokendial rather than by the
/// desktop. A plain window, so Do not disturb never swallows it.
/// </summary>
/// <remarks>
/// The Avalonia twin of windows/Tokendial.App/Alerts/BannerSink.cs, with one deliberate divergence. The
/// Windows card is painted in the system accent colour; no such colour is exposed on Cinnamon - the portal's
/// accent-color setting is newer than Mint 22 and absent here - so the card uses Tokendial's own accent. The
/// ink is still chosen by the same WCAG contrast rule, so the divergence is the palette and nothing else.
/// </remarks>
public sealed class BannerSink : IAlertSink
{
    private readonly Func<string, ProviderReading?> reading;
    private readonly Func<string, Activity?> activity;
    private readonly Func<DateTimeOffset> now;
    private BannerHost? host;

    public BannerSink(Func<string, ProviderReading?> reading, Func<string, Activity?> activity, Func<DateTimeOffset>? now = null)
    {
        this.reading = reading;
        this.activity = activity;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The user clicked a banner's body.</summary>
    public event Action<Alert>? Opened;

    public void Deliver(Alert alert)
    {
        var current = reading(alert.Provider);
        var (title, body) = AlertCopy.Compose(alert, current, activity(alert.Provider), now());
        var fraction = alert.Pct is int pct ? pct / 100.0 : current?.HeadlineFraction;
        Log.Alerts.Info($"banner: {title} — {body}");
        Dispatcher.UIThread.Post(() =>
        {
            host ??= new BannerHost();
            host.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            host.Add(new Banner(title, body, fraction, alert.Kind, alert.Provider, () => Opened?.Invoke(alert)));
        });
    }

    public void Close() => Dispatcher.UIThread.Post(() => { host?.Close(); host = null; });
}

/// <summary>Ink that stays legible on whatever the card is filled with.</summary>
public static class SystemLook
{
    /// <summary>
    /// The theme picks the ink; the fill can veto it. A dark interface wants white, a light one near-black,
    /// but a dark fill under a light theme would hide dark text, so below 3:1 the ink flips.
    /// </summary>
    public static Color InkFor(Color fill, bool darkApps)
    {
        var white = Colors.White;
        var black = Color.FromRgb(0x1A, 0x1B, 0x1E);
        var preferred = darkApps ? white : black;
        if (Contrast(preferred, fill) >= 3) return preferred;
        return preferred == white ? black : white;
    }

    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a) + 0.05;
        var lb = Luminance(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}

/// <summary>
/// A transparent, never-focused column in the top-right corner of the work area that stacks banners.
/// </summary>
/// <remarks>
/// Sized to its contents rather than given the whole column height: the window takes clicks across its whole
/// area, and a tall transparent window would swallow them everywhere the cards are not. Only the ten pixels
/// between two cards are dead, which is the price of not needing an input shape here.
/// </remarks>
public sealed class BannerHost : Window
{
    public const double CardWidth = 360;
    private const double EdgeGap = 16;

    private readonly StackPanel stack = new() { Spacing = 10 };

    public BannerHost()
    {
        Title = "Tokendial banners";
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = CardWidth;
        SizeToContent = SizeToContent.Height;
        Content = stack;

        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is not { } handle) return;
            X11.MakeNotification(handle);
            X11.RefuseFocus(handle);
            Place();
        };
    }

    public void Add(Banner banner)
    {
        banner.Dismissed += () =>
        {
            stack.Children.Remove(banner);
            if (stack.Children.Count == 0) Hide();
            else Place();
        };
        stack.Children.Insert(0, banner);
        if (!IsVisible) Show();
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
        banner.Enter();
    }

    private void Place()
    {
        var scale = Screens.Primary?.Scaling ?? 1.0;
        var work = X11.WorkArea() ?? (0, 0, (int)(Screens.Primary?.Bounds.Width ?? 1920), (int)(Screens.Primary?.Bounds.Height ?? 1080));
        var width = (int)Math.Round(CardWidth * scale);
        var gap = (int)Math.Round(EdgeGap * scale);
        Position = new PixelPoint(work.Item1 + work.Item3 - width - gap, work.Item2 + gap);
    }
}

/// <summary>One notification card. Lives eight seconds, longer while the cursor is on it.</summary>
public sealed class Banner : Border
{
    private static readonly TimeSpan Life = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AfterHover = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer timer = new();
    private bool leaving;

    public Banner(string title, string body, double? fraction, AlertKind kind, string providerId, Action open)
    {
        var accent = Color.FromRgb(0x34, 0xD3, 0x99);
        var ink = SystemLook.InkFor(accent, darkApps: false);
        var text = new SolidColorBrush(ink);
        var faint = new SolidColorBrush(Color.FromArgb(90, ink.R, ink.G, ink.B));

        Width = BannerHost.CardWidth;
        Padding = new Thickness(14, 12, 12, 12);
        CornerRadius = new CornerRadius(10);
        Background = new SolidColorBrush(Color.FromArgb(235, accent.R, accent.G, accent.B));
        BorderBrush = faint;
        BorderThickness = new Thickness(1);
        Cursor = new Cursor(StandardCursorType.Hand);
        Opacity = 0;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];

        var dial = new Dial(34, 3.5) { Fill = text, Track = faint, Hollow = fraction is null };
        if (fraction is double f) dial.Fraction = Math.Clamp(f, 0, 1);
        if (kind == AlertKind.Waiting) { dial.Hollow = false; dial.Fraction = 1; }
        var mark = new MarkView(providerId, 16)
        {
            Fill = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Avalonia.Controls.Panel
        {
            Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top, Children = { dial, mark }
        };

        var titleBlock = Panel.Text.Make(title, 14, text, FontWeight.SemiBold);
        Panel.Text.Wrap(titleBlock);
        var bodyBlock = Panel.Text.Make(body, 12, text);
        Panel.Text.Wrap(bodyBlock);
        bodyBlock.Opacity = 0.85;
        bodyBlock.Margin = new Thickness(0, 2, 0, 0);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { titleBlock, bodyBlock } };

        var cross = Panel.Text.Make("✕", 12, text, align: TextAlignment.Center);
        cross.Opacity = 0.7;
        cross.VerticalAlignment = VerticalAlignment.Center;
        var close = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -2, 0, 0), Child = cross
        };
        close.PointerEntered += (_, _) => close.Background = faint;
        close.PointerExited += (_, _) => close.Background = Brushes.Transparent;
        close.PointerReleased += (_, e) => { e.Handled = true; Leave(); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(badge);
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        Child = grid;

        PointerReleased += (_, _) => { open(); Leave(); };
        PointerEntered += (_, _) => timer.Stop();
        PointerExited += (_, _) => { timer.Interval = AfterHover; timer.Start(); };
        timer.Interval = Life;
        timer.Tick += (_, _) => Leave();
    }

    public event Action? Dismissed;

    public void Enter()
    {
        timer.Start();
        Opacity = 1;
    }

    private void Leave()
    {
        if (leaving) return;
        leaving = true;
        timer.Stop();
        IsHitTestVisible = false;
        Opacity = 0;
        // Long enough for the fade to finish; removing the card immediately would cut it off.
        DispatcherTimer.RunOnce(() => Dismissed?.Invoke(), TimeSpan.FromMilliseconds(200));
    }
}

/// <summary>
/// Routes each alert to the desktop's notifications, to Tokendial's banners, or to the desktop with banners
/// as the fallback. The only place the delivery preference is read.
/// </summary>
/// <remarks>
/// The third mode is "both" on Windows because it can ask whether Focus Assist swallowed the toast. There is
/// no portable equivalent of that question on Linux, so it means what it can honestly mean here: send it to
/// the desktop, and draw a banner as well when no notification daemon answered.
/// </remarks>
public sealed class AlertRouter : IAlertSink
{
    private readonly NotifySink notifications;
    private readonly BannerSink banners;
    private readonly Func<Core.Settings.AlertDelivery> mode;

    public AlertRouter(NotifySink notifications, BannerSink banners, Func<Core.Settings.AlertDelivery> mode)
    {
        this.notifications = notifications;
        this.banners = banners;
        this.mode = mode;
    }

    public void Deliver(Alert alert)
    {
        switch (mode())
        {
            case Core.Settings.AlertDelivery.WindowsToasts:
                notifications.Deliver(alert);
                break;
            case Core.Settings.AlertDelivery.TokendialBanners:
                banners.Deliver(alert);
                break;
            default:
                notifications.Deliver(alert);
                if (!notifications.Available) banners.Deliver(alert);
                break;
        }
    }
}
