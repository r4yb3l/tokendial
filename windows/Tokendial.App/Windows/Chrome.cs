using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Tokendial.App.Panel;

namespace Tokendial.App.Windows;

/// <summary>The settings design system, built in code: a surface scale from near-black to slate, one brand green, and controls drawn to match.</summary>
public static class Chrome
{
    /// <summary>One look for the windows: the surface scale, the text inks and the translucent card fills built from them.</summary>
    public sealed record Look(Brush WindowBackground, Brush Surface850, Brush Surface800, Brush Surface750, Brush Surface700, Brush Surface600,
        Brush Slate100, Brush Slate200, Brush Slate300, Brush Slate400, Brush Slate500, Brush Strong, Brush Divider, Brush TitleBarFill,
        Brush Sheet, Brush SheetStrong, Brush SheetSoft, Brush SheetFaint, Brush Edge, Brush EdgeSoft, Brush Line, Brush LineSoft, Brush LineFaint,
        Brush Well, Brush WellStrong, Brush HoverWash, Brush ButtonEdge, Brush ActiveCard);

    public static readonly Look DarkLook = new(
        WindowBackground: Rgb(0x0B, 0x0E, 0x14), Surface850: Rgb(0x11, 0x15, 0x1D), Surface800: Rgb(0x17, 0x1C, 0x26), Surface750: Rgb(0x1D, 0x23, 0x31), Surface700: Rgb(0x26, 0x2F, 0x40), Surface600: Rgb(0x38, 0x43, 0x58),
        Slate100: Rgb(0xF1, 0xF5, 0xF9), Slate200: Rgb(0xE2, 0xE8, 0xF0), Slate300: Rgb(0xCB, 0xD5, 0xE1), Slate400: Rgb(0x94, 0xA3, 0xB8), Slate500: Rgb(0x64, 0x74, 0x8B), Strong: Brushes.White,
        Divider: Rgba(0x26, 0x2F, 0x40, 0.6), TitleBarFill: Rgba(0x11, 0x15, 0x1D, 0.8),
        Sheet: Rgba(0x11, 0x15, 0x1D, 0.9), SheetStrong: Rgba(0x11, 0x15, 0x1D, 0.8), SheetSoft: Rgba(0x11, 0x15, 0x1D, 0.6), SheetFaint: Rgba(0x11, 0x15, 0x1D, 0.4),
        Edge: Rgba(0x1D, 0x23, 0x31, 0.7), EdgeSoft: Rgba(0x1D, 0x23, 0x31, 0.5), Line: Rgba(0x26, 0x2F, 0x40, 0.6), LineSoft: Rgba(0x26, 0x2F, 0x40, 0.5), LineFaint: Rgba(0x26, 0x2F, 0x40, 0.4),
        Well: Rgba(0x17, 0x1C, 0x26, 0.4), WellStrong: Rgba(0x17, 0x1C, 0x26, 0.8), HoverWash: Rgba(0x17, 0x1C, 0x26, 0.4), ButtonEdge: Rgba(0x38, 0x43, 0x58, 0.7),
        ActiveCard: Gradient(Color.FromArgb(0xD9, 0x17, 0x1C, 0x26), Color.FromArgb(0xF2, 0x11, 0x15, 0x1D)));

    public static readonly Look LightLook = new(
        WindowBackground: Rgb(0xE9, 0xED, 0xF3), Surface850: Rgb(0xFF, 0xFF, 0xFF), Surface800: Rgb(0xF1, 0xF4, 0xF8), Surface750: Rgb(0xE1, 0xE7, 0xEF), Surface700: Rgb(0xD2, 0xDA, 0xE5), Surface600: Rgb(0xB9, 0xC2, 0xCF),
        Slate100: Rgb(0x0F, 0x17, 0x2A), Slate200: Rgb(0x1E, 0x29, 0x3B), Slate300: Rgb(0x33, 0x41, 0x55), Slate400: Rgb(0x47, 0x55, 0x69), Slate500: Rgb(0x64, 0x74, 0x8B), Strong: Rgb(0x0F, 0x17, 0x2A),
        Divider: Rgba(0x0F, 0x17, 0x2A, 0.10), TitleBarFill: Rgba(0xFF, 0xFF, 0xFF, 0.85),
        Sheet: Rgba(0xFF, 0xFF, 0xFF, 0.95), SheetStrong: Rgba(0xFF, 0xFF, 0xFF, 0.9), SheetSoft: Rgba(0xFF, 0xFF, 0xFF, 0.8), SheetFaint: Rgba(0xFF, 0xFF, 0xFF, 0.6),
        Edge: Rgba(0xD2, 0xDA, 0xE5, 0.9), EdgeSoft: Rgba(0xD2, 0xDA, 0xE5, 0.6), Line: Rgba(0xC5, 0xCE, 0xDA, 0.8), LineSoft: Rgba(0xC5, 0xCE, 0xDA, 0.6), LineFaint: Rgba(0xC5, 0xCE, 0xDA, 0.4),
        Well: Rgba(0xE1, 0xE7, 0xEF, 0.6), WellStrong: Rgb(0xE1, 0xE7, 0xEF), HoverWash: Rgba(0x0F, 0x17, 0x2A, 0.05), ButtonEdge: Rgba(0xB9, 0xC2, 0xCF, 0.8),
        ActiveCard: Gradient(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF5, 0xF8, 0xFB)));

    private static Look current = DarkLook;

    public static void Use(bool dark) => current = dark ? DarkLook : LightLook;

    public static Brush WindowBackground => current.WindowBackground;
    public static Brush Surface850 => current.Surface850;
    public static Brush Surface800 => current.Surface800;
    public static Brush Surface750 => current.Surface750;
    public static Brush Surface700 => current.Surface700;
    public static Brush Surface600 => current.Surface600;
    public static Brush Panel => current.Surface850;
    public static Brush Control => current.Surface750;
    public static Brush ControlHover => current.Surface700;
    public static Brush Slate100 => current.Slate100;
    public static Brush Slate200 => current.Slate200;
    public static Brush Slate300 => current.Slate300;
    public static Brush Slate400 => current.Slate400;
    public static Brush Slate500 => current.Slate500;
    public static Brush Strong => current.Strong;
    public static Brush Divider => current.Divider;
    public static Brush TitleBarFill => current.TitleBarFill;
    public static Brush Sheet => current.Sheet;
    public static Brush SheetStrong => current.SheetStrong;
    public static Brush SheetSoft => current.SheetSoft;
    public static Brush SheetFaint => current.SheetFaint;
    public static Brush Edge => current.Edge;
    public static Brush EdgeSoft => current.EdgeSoft;
    public static Brush Line => current.Line;
    public static Brush LineSoft => current.LineSoft;
    public static Brush LineFaint => current.LineFaint;
    public static Brush Well => current.Well;
    public static Brush WellStrong => current.WellStrong;
    public static Brush HoverWash => current.HoverWash;
    public static Brush ButtonEdge => current.ButtonEdge;
    public static Brush ActiveCard => current.ActiveCard;

    public static Brush Accent => Theme.Ample;
    public static readonly Brush Brand500 = Rgb(0x10, 0xB9, 0x81);
    public static readonly Brush BrandFaint = Rgba(0x10, 0xB9, 0x81, 0.10);
    public static readonly Brush BrandSoft = Rgba(0x10, 0xB9, 0x81, 0.20);
    public static readonly Brush BrandLine = Rgba(0x10, 0xB9, 0x81, 0.40);
    public static readonly Brush BrandLineMid = Rgba(0x10, 0xB9, 0x81, 0.50);
    public static readonly Brush BrandLineStrong = Rgba(0x10, 0xB9, 0x81, 0.60);
    public static readonly Brush OnBrand = Rgb(0x06, 0x2B, 0x1F);
    public static readonly Brush Rose = Rgb(0xFD, 0xA4, 0xAF);
    public static readonly Brush RoseSoft = Rgba(0xF4, 0x3F, 0x5E, 0.20);
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public const double TitleBarHeight = 44;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, FrameParts> frames = new();
    private sealed record FrameParts(string? Badge, FrameworkElement? Status);

    private static string Hex(Brush brush) => ((SolidColorBrush)brush).Color.ToString();

    private static Brush Gradient(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }

    public static Brush Rgb(byte r, byte g, byte b) => Frozen(Color.FromRgb(r, g, b));
    public static Brush Rgba(byte r, byte g, byte b, double alpha) => Frozen(Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b));

    /// <summary>A window with the redesign's title bar: logo, title, version badge, an optional status pill, and rounded caption buttons. The standard frame stays underneath so dragging, snapping and rounded corners keep working.</summary>
    public static Window Frame(string title, double width, double height, UIElement content, string? badge = null, FrameworkElement? status = null)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 480,
            MinHeight = 360,
            Background = WindowBackground,
            Foreground = Slate200,
            FontFamily = Theme.Font,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            ResizeMode = ResizeMode.CanResize,
            SnapsToDevicePixels = true,
            WindowStyle = WindowStyle.SingleBorderWindow
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = TitleBarHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        var root = new Grid { Name = "frameRoot" };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(TitleBar(window, badge, status));
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        window.Content = root;
        frames.AddOrUpdate(window, new FrameParts(badge, status));
        window.StateChanged += (_, _) => root.Margin = window.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        return window;
    }

    /// <summary>Rebuild the title bar and the page in the current language and look; the status element carries over.</summary>
    public static void Rebuild(Window window, UIElement content)
    {
        if (window.Content is not Grid root || !frames.TryGetValue(window, out var parts)) return;
        if (parts.Status?.Parent is System.Windows.Controls.Panel host) host.Children.Remove(parts.Status);
        root.Children.Clear();
        root.Children.Add(TitleBar(window, parts.Badge, parts.Status));
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        window.Background = WindowBackground;
        window.Foreground = Slate200;
    }

    private static Grid TitleBar(Window window, string? badge, FrameworkElement? status)
    {
        var bar = new Grid { Background = TitleBarFill };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        left.Children.Add(Logo());
        var title = Text.Make(window.Title, 12, Slate200, FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(8, 0, 0, 0);
        title.SetBinding(TextBlock.TextProperty, new Binding("Title") { Source = window });
        left.Children.Add(title);
        if (badge is not null)
        {
            var pill = Pill(badge.ToUpperInvariant(), Slate400, Surface800, LineSoft, mono: true, size: 10, radius: 4);
            pill.Margin = new Thickness(8, 0, 0, 0);
            pill.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(pill);
        }
        bar.Children.Add(left);

        if (status is not null)
        {
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(status, 1);
            bar.Children.Add(status);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(buttons, 2);
        buttons.Children.Add(CaptionButton("M5 12h14", () => window.WindowState = WindowState.Minimized, false));
        var maximize = CaptionButton("M4 4h16v16H4z", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false);
        buttons.Children.Add(maximize);
        buttons.Children.Add(CaptionButton("M18 6L6 18M6 6l12 12", window.Close, true));
        bar.Children.Add(buttons);

        var underline = new Border { Height = 1, Background = LineSoft, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(underline, 3);
        bar.Children.Add(underline);
        bar.FlowDirection = FlowDirection.LeftToRight;
        return bar;
    }

    /// <summary>A 20 px disc with the dial arc inside and a breathing green dot on its shoulder.</summary>
    private static Grid Logo()
    {
        var logo = new Grid { Width = 22, Height = 22 };
        logo.Children.Add(new Ellipse { Width = 20, Height = 20, Fill = Surface800, Stroke = ButtonEdge, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });
        logo.Children.Add(new Dial(12, 2) { Fill = Accent, Fraction = 0.66, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 0, 4) });
        logo.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = WindowBackground, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
        var dot = new Ellipse { Width = 6, Height = 6, Fill = Accent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0) };
        if (!Theme.ReduceMotion)
            dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, new Duration(TimeSpan.FromSeconds(1.1))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        logo.Children.Add(dot);
        return logo;
    }

    private static Button CaptionButton(string glyph, Action click, bool danger)
    {
        var button = new Button { Width = 28, Height = 28, Margin = new Thickness(4, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Slate400, Focusable = false };
        var icon = Icon(glyph, danger ? 14 : 13, Slate400);
        icon.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { Source = button });
        button.Content = icon;
        button.Template = HoverTemplate(8, danger ? RoseSoft : Surface750, danger ? Rose : Slate200);
        button.Click += (_, _) => click();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        return button;
    }

    /// <summary>A stroked 24-unit SVG path scaled to the given size.</summary>
    public static Path Icon(string data, double size, Brush stroke, double thickness = 2)
    {
        return new Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static ScrollViewer Scroll(UIElement content)
    {
        var viewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0)
        };
        viewer.Resources.Add(typeof(ScrollBar), ThinScrollBar());
        return viewer;
    }

    /// <summary>A 6 px thumb on an invisible track, no arrows: the scrollbar of the design.</summary>
    private static Style ThinScrollBar()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Setter Property="Width" Value="8"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Border Background="Transparent" Width="8">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb>
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border x:Name="pill" Background="{THUMB}" CornerRadius="3" Margin="1,0,1,0"/>
                                <ControlTemplate.Triggers>
                                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="pill" Property="Background" Value="{THUMB_HOVER}"/></Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                      </Track>
                    </Border>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml.Replace("{THUMB}", Hex(Surface700)).Replace("{THUMB_HOVER}", Hex(Surface600)));
    }

    public static TextBlock Heading(string text) => Text.Make(text, 13, Strong, FontWeights.SemiBold, display: true);

    public static TextBlock Body(string text, bool wrap = true)
    {
        var block = Text.Make(text, 12, Slate400);
        if (wrap) { block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.None; }
        return block;
    }

    public static TextBlock Label(string text) => Text.Make(text, 12, Slate200, FontWeights.Medium);

    /// <summary>"PROVEEDORES  (subtitle)" with something aligned to the right.</summary>
    public static Grid SectionTitle(string title, string? subtitle = null, FrameworkElement? right = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Text.Make(title.ToUpper(Core.I18n.Strings.Culture), 13, Strong, FontWeights.SemiBold, display: true));
        if (subtitle is not null)
        {
            var sub = Text.Make(subtitle, 12, Slate400);
            sub.Margin = new Thickness(8, 0, 0, 0);
            sub.VerticalAlignment = VerticalAlignment.Center;
            words.Children.Add(sub);
        }
        grid.Children.Add(words);
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        return grid;
    }

    /// <summary>The quieter uppercase heading of secondary sections.</summary>
    public static TextBlock SmallTitle(string text) => Text.Make(text.ToUpper(Core.I18n.Strings.Culture), 11, Slate400, FontWeights.SemiBold);

    public static Border Card(UIElement body, Brush background, Brush border, double padding = 14, double radius = 12) =>
        new() { Background = background, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(padding), Child = body };

    public static Border Pill(string text, Brush foreground, Brush background, Brush border, bool mono = false, double size = 11, double radius = 999)
    {
        var block = Text.Make(text, size, foreground, FontWeights.Medium);
        if (mono) block.FontFamily = Mono;
        return new Border { Background = background, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(8, 2, 8, 2), Child = block };
    }

    public static Border Rule(Thickness margin) => new() { Height = 1, Background = Divider, Margin = margin };

    public static Border Section(string title, string? description, UIElement body)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title));
        if (description is not null)
        {
            var d = Body(description);
            d.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(d);
        }
        if (body is FrameworkElement fe) fe.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(body);
        var card = Card(stack, SheetSoft, Edge, 16);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    /// <summary>A bare 16 px check square, brand green when on.</summary>
    public static CheckBox CheckBox(bool value, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = value, Template = ToggleTemplate(round: false, size: 16, glyph: 10, hoverBackground: null, padding: new Thickness(0), radius: 0) };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    public static CheckBox Check(string label, bool value, Action<bool> changed, string? hint = null)
    {
        var box = CheckBox(value, changed);
        box.Content = Caption(label, hint, Slate200);
        box.Margin = new Thickness(0, 2, 0, 2);
        return box;
    }

    /// <summary>A labelled row whose whole question is on or off, with the switch at its far end.</summary>
    public static UIElement SwitchRow(string label, string? hint, bool value, Action<bool> changed)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = Caption(label, hint, Slate200, 11);
        caption.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(caption);
        var toggle = Switch(value, changed);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>A radio row that lights up under the cursor.</summary>
    public static RadioButton Radio(string group, string label, bool value, Action changed, string? hint = null)
    {
        var button = new RadioButton { GroupName = group, IsChecked = value, Template = ToggleTemplate(round: true, size: 15, glyph: 6, hoverBackground: HoverWash, padding: new Thickness(8), radius: 8) };
        button.Content = Caption(label, hint, Slate200, 11);
        button.Checked += (_, _) => changed();
        return button;
    }

    /// <summary>A whole card that is one option: bordered in brand green when chosen.</summary>
    public static RadioButton RadioCard(string group, string label, bool value, Action changed, string? hint = null)
    {
        var button = new RadioButton { GroupName = group, IsChecked = value, Margin = new Thickness(0, 0, 0, 10) };
        button.Template = CardTemplate();
        var title = Text.Make(label, 12, value ? Strong : Slate200, value ? FontWeights.SemiBold : FontWeights.Medium);
        var caption = new StackPanel();
        caption.Children.Add(title);
        if (hint is not null) { var h = Body(hint); h.FontSize = 11; h.Margin = new Thickness(0, 2, 0, 0); caption.Children.Add(h); }
        button.Content = caption;
        button.Checked += (_, _) => { title.Foreground = Strong; title.FontWeight = FontWeights.SemiBold; changed(); };
        button.Unchecked += (_, _) => { title.Foreground = Slate200; title.FontWeight = FontWeights.Medium; };
        return button;
    }

    /// <summary>A small selectable chip, for the language picker. The content may be a bare string or a
    /// row of its own, so a language can carry its flag.</summary>
    public static RadioButton Chip(string group, object content, bool value, Action changed)
    {
        var button = new RadioButton { GroupName = group, IsChecked = value, Content = content, FontSize = 11, Foreground = Slate400, Margin = new Thickness(0, 0, 8, 8), Template = ChipTemplate(round: true) };
        button.Checked += (_, _) => changed();
        return button;
    }

    /// <summary>A chip that toggles on its own, for the pickers where more than one may be on at a time. The caller wires Click, so restoring the box after a refused change cannot re-enter the handler.</summary>
    public static CheckBox CheckChip(string label, bool value) =>
        new() { IsChecked = value, Content = label, FontSize = 11, Foreground = Slate400, Margin = new Thickness(0, 0, 8, 8), Template = ChipTemplate(round: false) };

    /// <summary>An on/off switch, for a row whose whole question is whether Tokendial reads that tool.</summary>
    public static ToggleButton Switch(bool value, Action<bool> changed)
    {
        var button = new ToggleButton { IsChecked = value, Template = SwitchTemplate(), Focusable = false, Cursor = System.Windows.Input.Cursors.Hand };
        button.Checked += (_, _) => changed(true);
        button.Unchecked += (_, _) => changed(false);
        return button;
    }

    /// <summary>A picture-first option: a small drawing of what the choice does, with its name under it.
    /// The label takes its colour from the tile, so selecting one darkens its own name.</summary>
    public static RadioButton Tile(string group, UIElement diagram, string label, bool value, Action changed)
    {
        var body = new StackPanel();
        diagram.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 9));
        body.Children.Add(diagram);
        body.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontFamily = Theme.Font,
            FontWeight = FontWeights.Medium,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var button = new RadioButton { GroupName = group, IsChecked = value, Content = body, Foreground = Slate400, Margin = new Thickness(0, 0, 8, 8), Template = TileTemplate(), Cursor = System.Windows.Input.Cursors.Hand };
        button.Checked += (_, _) => changed();
        return button;
    }

    /// <summary>The compact row of dials the dock shows before the cursor reaches it.</summary>
    public static UIElement CompactDiagram(bool wide)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < 3; i++)
            row.Children.Add(new Border { Width = wide ? 14 : 9, Height = wide ? 14 : 5, CornerRadius = new CornerRadius(wide ? 3 : 2), Background = Surface600, Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0) });
        return Screen(row, 38, new Thickness(0, 6, 0, 0));
    }

    /// <summary>Hidden: nothing at the edge but the tray icon.</summary>
    public static UIElement TrayDiagram()
    {
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Surface600, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        return Screen(dot, 38, new Thickness(0, 6, 0, 0));
    }

    /// <summary>A screen with the dock drawn against one of its four edges.</summary>
    public static UIElement EdgeDiagram(bool vertical, bool far, bool selected)
    {
        var bar = new Border
        {
            Background = selected ? Brand500 : Surface600,
            CornerRadius = new CornerRadius(2),
            Width = vertical ? double.NaN : 5,
            Height = vertical ? 5 : double.NaN,
            HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : far ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = vertical ? far ? VerticalAlignment.Bottom : VerticalAlignment.Top : VerticalAlignment.Stretch
        };
        return Screen(bar, 30, new Thickness(3));
    }

    private static Border Screen(UIElement child, double height, Thickness padding) => new()
    {
        Height = height,
        Background = WindowBackground,
        BorderBrush = Edge,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Padding = padding,
        Child = child
    };

    private static StackPanel Caption(string label, string? hint, Brush colour, double hintSize = 11)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text.Make(label, 12, colour, FontWeights.Medium));
        if (hint is not null) { var h = Body(hint); h.FontSize = hintSize; h.Margin = new Thickness(0, 1, 0, 0); stack.Children.Add(h); }
        return stack;
    }

    public static Button Button(string label, Action click, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Background = primary ? Brand500 : Surface750,
            BorderBrush = primary ? Brand500 : ButtonEdge,
            BorderThickness = new Thickness(1),
            Foreground = primary ? OnBrand : Slate200,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Medium,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false
        };
        button.Template = ButtonTemplate(8, primary ? Accent : Surface700);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A full-width action with an icon before its label.</summary>
    public static Button WideButton(string icon, string label, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(Icon(icon, 15, Accent));
        var text = Text.Make(label, 12, Slate200, FontWeights.Medium);
        text.Margin = new Thickness(8, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(text);
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(12, 8, 12, 8),
            Background = Surface800,
            BorderBrush = Surface700,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Template = ButtonTemplate(12, Surface750)
        };
        button.Click += (_, _) => click();
        return button;
    }

    public static Grid Row(string label, UIElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text.Make(label, 12, Slate300));
        if (hint is not null) { var h = Body(hint); h.FontSize = 11; stack.Children.Add(h); }
        grid.Children.Add(stack);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    private static ControlTemplate ButtonTemplate(double radius, Brush hover)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "bg");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, hover, "bg"));
        template.Triggers.Add(over);
        return template;
    }

    private static ControlTemplate HoverTemplate(double radius, Brush hoverBackground, Brush hoverForeground)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "bg");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, hoverBackground, "bg"));
        over.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, hoverForeground));
        template.Triggers.Add(over);
        return template;
    }

    /// <summary>The check square or radio disc: dark with a slate edge, brand green with a white glyph when on.</summary>
    private static (FrameworkElementFactory Box, string BoxName, string GlyphName) Indicator(bool round, double size, double glyph)
    {
        var box = new FrameworkElementFactory(typeof(Border), "box");
        box.SetValue(FrameworkElement.WidthProperty, size);
        box.SetValue(FrameworkElement.HeightProperty, size);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(round ? size / 2 : 4));
        box.SetValue(Border.BackgroundProperty, round ? WindowBackground : Surface800);
        box.SetValue(Border.BorderBrushProperty, Surface600);
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (round)
        {
            var dot = new FrameworkElementFactory(typeof(Ellipse), "glyph");
            dot.SetValue(FrameworkElement.WidthProperty, glyph);
            dot.SetValue(FrameworkElement.HeightProperty, glyph);
            dot.SetValue(Shape.FillProperty, Brushes.White);
            dot.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(dot);
        }
        else
        {
            var check = new FrameworkElementFactory(typeof(Path), "glyph");
            check.SetValue(Path.DataProperty, Geometry.Parse("M5 13l4 4L19 7"));
            check.SetValue(Shape.StrokeProperty, Brushes.White);
            check.SetValue(Shape.StrokeThicknessProperty, 2.2);
            check.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            check.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            check.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            check.SetValue(Shape.StretchProperty, Stretch.Uniform);
            check.SetValue(FrameworkElement.WidthProperty, glyph);
            check.SetValue(FrameworkElement.HeightProperty, glyph);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(check);
        }
        return (box, "box", "glyph");
    }

    private static ControlTemplate ToggleTemplate(bool round, double size, double glyph, Brush? hoverBackground, Thickness padding, double radius)
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var bg = new FrameworkElementFactory(typeof(Border), "bg");
        bg.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        bg.SetValue(Border.PaddingProperty, padding);
        var grid = new FrameworkElementFactory(typeof(Grid));
        var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        grid.AppendChild(c0);
        grid.AppendChild(c1);
        var (box, boxName, glyphName) = Indicator(round, size, glyph);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 0));
        grid.AppendChild(box);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(Grid.ColumnProperty, 1);
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(presenter);
        bg.AppendChild(grid);
        template.VisualTree = bg;
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brand500, boxName));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Brand500, boxName));
        on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, glyphName));
        template.Triggers.Add(on);
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BorderBrushProperty, Slate400, boxName));
        if (hoverBackground is not null) over.Setters.Add(new Setter(Border.BackgroundProperty, hoverBackground, "bg"));
        template.Triggers.Add(over);
        return template;
    }

    private static ControlTemplate CardTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var card = new FrameworkElementFactory(typeof(Border), "card");
        card.SetValue(Border.BackgroundProperty, SheetFaint);
        card.SetValue(Border.BorderBrushProperty, Surface750);
        card.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        card.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        card.SetValue(Border.PaddingProperty, new Thickness(12));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        grid.AppendChild(c0);
        grid.AppendChild(c1);
        var (box, boxName, glyphName) = Indicator(round: true, size: 16, glyph: 6);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 0));
        grid.AppendChild(box);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(Grid.ColumnProperty, 1);
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 0, 0));
        grid.AppendChild(presenter);
        card.AppendChild(grid);
        template.VisualTree = card;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BorderBrushProperty, Surface600, "card"));
        template.Triggers.Add(over);
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BorderBrushProperty, BrandLine, "card"));
        on.Setters.Add(new Setter(Border.BackgroundProperty, Sheet, "card"));
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brand500, boxName));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Brand500, boxName));
        on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, glyphName));
        template.Triggers.Add(on);
        return template;
    }

    private static ControlTemplate ChipTemplate(bool round)
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var chip = new FrameworkElementFactory(typeof(Border), "chip");
        chip.SetValue(Border.BackgroundProperty, Well);
        chip.SetValue(Border.BorderBrushProperty, Line);
        chip.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chip.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var (box, boxName, glyphName) = Indicator(round, size: 12, glyph: round ? 4 : 8);
        row.AppendChild(box);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(presenter);
        chip.AppendChild(row);
        template.VisualTree = chip;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Slate200));
        template.Triggers.Add(over);
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Slate200));
        on.Setters.Add(new Setter(Border.BackgroundProperty, WellStrong, "chip"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, BrandLineMid, "chip"));
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brand500, boxName));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Brand500, boxName));
        on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, glyphName));
        template.Triggers.Add(on);
        return template;
    }

    private static ControlTemplate SwitchTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var track = new FrameworkElementFactory(typeof(Border), "track");
        track.SetValue(FrameworkElement.WidthProperty, 34.0);
        track.SetValue(FrameworkElement.HeightProperty, 20.0);
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        track.SetValue(Border.BackgroundProperty, Well);
        track.SetValue(Border.BorderBrushProperty, ButtonEdge);
        track.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        track.SetValue(Border.PaddingProperty, new Thickness(2));
        var knob = new FrameworkElementFactory(typeof(Border), "knob");
        knob.SetValue(FrameworkElement.WidthProperty, 14.0);
        knob.SetValue(FrameworkElement.HeightProperty, 14.0);
        knob.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        knob.SetValue(Border.BackgroundProperty, Slate500);
        knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        track.AppendChild(knob);
        template.VisualTree = track;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BorderBrushProperty, Surface600, "track"));
        template.Triggers.Add(over);
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brand500, "track"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Brand500, "track"));
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "knob"));
        on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "knob"));
        template.Triggers.Add(on);
        return template;
    }

    private static ControlTemplate TileTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var tile = new FrameworkElementFactory(typeof(Border), "tile");
        tile.SetValue(Border.BackgroundProperty, SheetSoft);
        tile.SetValue(Border.BorderBrushProperty, Edge);
        tile.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        tile.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        tile.SetValue(Border.PaddingProperty, new Thickness(10));
        tile.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = tile;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Slate200));
        over.Setters.Add(new Setter(Border.BorderBrushProperty, ButtonEdge, "tile"));
        template.Triggers.Add(over);
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Strong));
        on.Setters.Add(new Setter(Border.BackgroundProperty, BrandFaint, "tile"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, BrandLine, "tile"));
        template.Triggers.Add(on);
        return template;
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
