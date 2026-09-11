using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Windows;

/// <summary>
/// The vocabulary the settings and welcome windows are written in: the surface scale, the text inks, and the
/// widgets built from them. The Avalonia twin of windows/Tokendial.App/Windows/Chrome.cs.
/// </summary>
/// <remarks>
/// The public signatures are deliberately identical to the WPF ones, even where an Avalonia-native shape
/// would differ. Roughly 1,270 lines of settings, welcome and install-assistant code are pure consumers of
/// this vocabulary and name no WPF type; keeping the names and argument lists the same turns those into a
/// port rather than a rewrite, and confines the real work to this file. Where WPF builds eight
/// ControlTemplates out of FrameworkElementFactory and Triggers, Avalonia uses pseudo-class selectors, which
/// is both the idiom here and considerably shorter - that is the one place the two genuinely diverge.
/// </remarks>
public static partial class Chrome
{
    /// <summary>One look for the windows: the surface scale, the text inks and the translucent card fills built from them.</summary>
    public sealed record Look(IBrush WindowBackground, IBrush Surface850, IBrush Surface800, IBrush Surface750, IBrush Surface700, IBrush Surface600,
        IBrush Slate100, IBrush Slate200, IBrush Slate300, IBrush Slate400, IBrush Slate500, IBrush Strong, IBrush Divider, IBrush TitleBarFill,
        IBrush Sheet, IBrush SheetStrong, IBrush SheetSoft, IBrush SheetFaint, IBrush Edge, IBrush EdgeSoft, IBrush Line, IBrush LineSoft, IBrush LineFaint,
        IBrush Well, IBrush WellStrong, IBrush HoverWash, IBrush ButtonEdge, IBrush ActiveCard);

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

    public static IBrush WindowBackground => current.WindowBackground;
    public static IBrush Surface850 => current.Surface850;
    public static IBrush Surface800 => current.Surface800;
    public static IBrush Surface750 => current.Surface750;
    public static IBrush Surface700 => current.Surface700;
    public static IBrush Surface600 => current.Surface600;
    public static IBrush Slate100 => current.Slate100;
    public static IBrush Slate200 => current.Slate200;
    public static IBrush Slate300 => current.Slate300;
    public static IBrush Slate400 => current.Slate400;
    public static IBrush Slate500 => current.Slate500;
    public static IBrush Strong => current.Strong;
    public static IBrush Divider => current.Divider;
    public static IBrush TitleBarFill => current.TitleBarFill;
    public static IBrush Sheet => current.Sheet;
    public static IBrush SheetStrong => current.SheetStrong;
    public static IBrush SheetSoft => current.SheetSoft;
    public static IBrush SheetFaint => current.SheetFaint;
    public static IBrush Edge => current.Edge;
    public static IBrush EdgeSoft => current.EdgeSoft;
    public static IBrush Line => current.Line;
    public static IBrush LineSoft => current.LineSoft;
    public static IBrush LineFaint => current.LineFaint;
    public static IBrush Well => current.Well;
    public static IBrush WellStrong => current.WellStrong;
    public static IBrush HoverWash => current.HoverWash;
    public static IBrush ButtonEdge => current.ButtonEdge;
    public static IBrush ActiveCard => current.ActiveCard;
    public static IBrush Accent => Theme.Ample;

    public static readonly IBrush BrandFaint = Rgba(0x10, 0xB9, 0x81, 0.10);
    public static readonly IBrush BrandSoft = Rgba(0x10, 0xB9, 0x81, 0.20);

    // Cascadia does not ship with Mint; the fallbacks are what a developer machine actually has.
    public static readonly FontFamily Mono = new("Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, monospace");

    public static IBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
    public static IBrush Rgba(byte r, byte g, byte b, double alpha) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b));

    private static IBrush Gradient(Color from, Color to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
    };

    // ---- text -------------------------------------------------------------------------------------

    public static TextBlock Heading(string text) => Panel.Text.Make(text, 13, Strong, FontWeight.SemiBold);

    public static TextBlock Body(string text, bool wrap = true)
    {
        var block = Panel.Text.Make(text, 12, Slate400);
        if (wrap) Panel.Text.Wrap(block);
        return block;
    }

    public static TextBlock Label(string text) => Panel.Text.Make(text, 12, Slate200, FontWeight.Medium);

    public static TextBlock SmallTitle(string text) =>
        Panel.Text.Make(text.ToUpper(Core.I18n.Strings.Culture), 11, Slate400, FontWeight.SemiBold);

    public static Grid SectionTitle(string title, string? subtitle = null, Control? right = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new StackPanel { Children = { SmallTitle(title) } };
        if (subtitle is not null) left.Children.Add(Body(subtitle));
        grid.Children.Add(left);
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            right.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(right);
        }
        return grid;
    }

    // ---- containers -------------------------------------------------------------------------------

    public static ScrollViewer Scroll(Control content)
    {
        // Top, not the default Stretch. A stretched child takes the viewport's height rather than its own,
        // so the extent equals the viewport, the scroll bar never appears and the wheel does nothing - the
        // content is simply clipped. This is the whole reason settings would not scroll.
        content.VerticalAlignment = VerticalAlignment.Top;
        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0)
        };
    }

    public static Border Card(Control body, IBrush background, IBrush border, double padding = 14, double radius = 12) =>
        new() { Child = body, Background = background, BorderBrush = border, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius), Padding = new Thickness(padding) };

    public static Border Rule(Thickness margin) => new() { Height = 1, Background = Divider, Margin = margin };

    public static Border Section(string title, string? description, Control body)
    {
        var stack = new StackPanel { Spacing = 10, Children = { SectionTitle(title, description) , body } };
        return Card(stack, Surface850, Edge, 16, 14);
    }

    public static Border Pill(string text, IBrush foreground, IBrush background, IBrush border, bool mono = false, double size = 11, double radius = 999)
    {
        var block = Panel.Text.Make(text, size, foreground, FontWeight.Medium);
        if (mono) block.FontFamily = Mono;
        return new Border
        {
            Child = block, Background = background, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius), Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static Avalonia.Controls.Shapes.Path Icon(string data, double size, IBrush stroke, double thickness = 2) => new()
    {
        Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        Width = size, Height = size, Stretch = Stretch.Uniform
    };

    /// <summary>The second line of a row: a size below the body text, as windows/Tokendial.App/Windows/Chrome.cs:568 sets it.</summary>
    public static TextBlock HintText(string text)
    {
        var block = Body(text);
        block.FontSize = 11;
        return block;
    }

    public static Grid Row(string label, Control control, string? hint = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 6) };
        // A gutter, so a hint that wraps stops short of the control instead of touching it.
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Children = { Label(label) } };
        if (hint is not null) left.Children.Add(HintText(hint));
        grid.Children.Add(left);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }
}
