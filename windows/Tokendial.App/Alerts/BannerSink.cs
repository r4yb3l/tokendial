using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Tokendial.App.Interop;
using Tokendial.App.Panel;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Alerts;

/// <summary>
/// Tokendial's own notification: a card in the top-right corner painted with the
/// Windows accent colour at 80 %, white text in dark mode and near-black in light
/// mode. A plain window, so Do not disturb and Focus Assist never swallow it.
/// </summary>
public sealed class BannerSink : IAlertSink
{
    private readonly Func<string, ProviderReading?> reading;
    private readonly Func<string, Activity?> activity;
    private readonly Func<DateTimeOffset> now;
    private readonly Dispatcher dispatcher;
    private BannerHost? host;

    public BannerSink(Dispatcher dispatcher, Func<string, ProviderReading?> reading, Func<string, Activity?> activity, Func<DateTimeOffset>? now = null)
    {
        this.dispatcher = dispatcher;
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
        dispatcher.BeginInvoke(() =>
        {
            host ??= new BannerHost();
            host.FlowDirection = Core.I18n.Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            host.Add(new Banner(title, body, fraction, alert.Kind, alert.Provider, () => Opened?.Invoke(alert)));
        });
    }

    public void Close() => dispatcher.BeginInvoke(() => host?.Close());
}

/// <summary>What the system looks like right now: accent colour and light or dark apps.</summary>
public static class SystemLook
{
    public static bool DarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light ? light == 0 : true;
        }
        catch (Exception) { return true; }
    }

    public static Color Accent()
    {
        try
        {
            var colour = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgb(colour.R, colour.G, colour.B);
        }
        catch (Exception)
        {
            var glass = SystemParameters.WindowGlassColor;
            return Color.FromRgb(glass.R, glass.G, glass.B);
        }
    }

    /// <summary>The theme picks the ink; the fill can veto it. Dark apps want white, light apps want near-black, but a black accent under a light theme would hide dark text, so below 3:1 the ink flips.</summary>
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

/// <summary>A transparent, non-activating column in the top-right corner of the work area that stacks banners.</summary>
public sealed class BannerHost : Window
{
    public const double Width_ = 360;
    public const double EdgeGap = 16;
    private readonly StackPanel stack = new() { VerticalAlignment = VerticalAlignment.Top, UseLayoutRounding = false };

    public BannerHost()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Title = "Tokendial banners";
        Content = stack;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeUnobtrusive(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook((_, msg, _, _, ref handled) =>
            {
                if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            });
            Place();
        };
    }

    public void Add(Banner banner)
    {
        banner.Dismissed += () =>
        {
            stack.Children.Remove(banner);
            if (stack.Children.Count == 0) Hide();
        };
        stack.Children.Insert(0, banner);
        if (!IsVisible) Show();
        Place();
        banner.Enter();
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var screen = Native.PrimaryScreen();
        var widthDip = Width_ + 2 * EdgeGap;
        var heightDip = Math.Min(screen.Work.Height / screen.Scale - EdgeGap, 720);
        var width = (int)Math.Round(widthDip * screen.Scale);
        var height = (int)Math.Round(heightDip * screen.Scale);
        stack.Margin = new Thickness(EdgeGap, EdgeGap * 0.75, EdgeGap, 0);
        Native.Place(hwnd, screen.Work.Right - width, screen.Work.Top, width, height, show: true);
    }
}

/// <summary>One notification card. Lives eight seconds, longer while the cursor is on it.</summary>
public sealed class Banner : Border
{
    private static readonly TimeSpan Life = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AfterHover = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer timer = new();
    private readonly TranslateTransform slide = new();
    private bool leaving;

    public Banner(string title, string body, double? fraction, AlertKind kind, string providerId, Action open)
    {
        var dark = SystemLook.DarkApps();
        var accent = SystemLook.Accent();
        var ink = SystemLook.InkFor(accent, dark);
        var lightInk = ink == Colors.White;
        var text = new SolidColorBrush(ink);
        text.Freeze();
        var faint = new SolidColorBrush(Color.FromArgb(lightInk ? (byte)90 : (byte)70, ink.R, ink.G, ink.B));
        faint.Freeze();

        Width = BannerHost.Width_;
        Margin = new Thickness(0, 0, 0, 10);
        Padding = new Thickness(14, 12, 12, 12);
        CornerRadius = new CornerRadius(10);
        Background = new SolidColorBrush(Color.FromArgb(204, accent.R, accent.G, accent.B));
        BorderBrush = faint;
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        RenderTransform = slide;
        Opacity = 0;
        UseLayoutRounding = false;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = lightInk ? 0.45 : 0.25, Color = Colors.Black };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dial = new Dial(34, 3.5) { Fill = text, Track = faint, Hollow = fraction is null };
        if (fraction is double f) dial.Fraction = Math.Clamp(f, 0, 1);
        if (kind == AlertKind.Waiting) { dial.Hollow = false; dial.Fraction = 1; }
        var mark = new MarkView(providerId, 16) { Fill = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        badge.Children.Add(dial);
        badge.Children.Add(mark);
        grid.Children.Add(badge);

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = Text.Make(title, 14, text, FontWeights.SemiBold, display: true);
        titleBlock.TextWrapping = TextWrapping.Wrap;
        titleBlock.TextTrimming = TextTrimming.None;
        lines.Children.Add(titleBlock);
        var bodyBlock = Text.Make(body, 12, text);
        bodyBlock.Opacity = 0.85;
        bodyBlock.TextWrapping = TextWrapping.Wrap;
        bodyBlock.TextTrimming = TextTrimming.None;
        bodyBlock.Margin = new Thickness(0, 2, 0, 0);
        lines.Children.Add(bodyBlock);
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);

        var close = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -2, 0, 0) };
        close.Child = Text.Make("✕", 12, text, align: TextAlignment.Center);
        ((TextBlock)close.Child).Opacity = 0.7;
        ((TextBlock)close.Child).VerticalAlignment = VerticalAlignment.Center;
        close.MouseEnter += (_, _) => close.Background = faint;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Leave(); };
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        Child = grid;

        MouseLeftButtonUp += (_, _) => { open(); Leave(); };
        MouseEnter += (_, _) => timer.Stop();
        MouseLeave += (_, _) => { timer.Interval = AfterHover; timer.Start(); };
        timer.Interval = Life;
        timer.Tick += (_, _) => Leave();
    }

    public event Action? Dismissed;

    public void Enter()
    {
        timer.Start();
        if (Theme.ReduceMotion) { Opacity = 1; return; }
        slide.X = 32;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, Theme.Crossfade));
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Theme.DurationOf(Theme.Glide)) { EasingFunction = Theme.Glide });
    }

    private void Leave()
    {
        if (leaving) return;
        leaving = true;
        timer.Stop();
        IsHitTestVisible = false;
        if (Theme.ReduceMotion) { Dismissed?.Invoke(); return; }
        var fade = new DoubleAnimation(0, Theme.Crossfade);
        fade.Completed += (_, _) => Dismissed?.Invoke();
        BeginAnimation(OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24, Theme.Crossfade));
    }
}

/// <summary>Routes each alert to Windows, to Tokendial's banners, or to Windows with banners as the fallback when Windows is silenced.</summary>
public sealed class AlertRouter : IAlertSink
{
    private readonly ToastSink toasts;
    private readonly BannerSink banners;
    private readonly Func<Core.Settings.AlertDelivery> mode;

    public AlertRouter(ToastSink toasts, BannerSink banners, Func<Core.Settings.AlertDelivery> mode)
    {
        this.toasts = toasts;
        this.banners = banners;
        this.mode = mode;
    }

    public void Deliver(Alert alert)
    {
        switch (mode())
        {
            case Core.Settings.AlertDelivery.WindowsToasts:
                toasts.Deliver(alert);
                break;
            case Core.Settings.AlertDelivery.TokendialBanners:
                banners.Deliver(alert);
                break;
            default:
                toasts.Deliver(alert);
                if (Native.NotificationsMuted()) banners.Deliver(alert);
                break;
        }
    }
}
