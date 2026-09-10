using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Tokendial.App.Windows;

/// <summary>
/// A 16x11 flag for the language picker, drawn from bands and crosses.
/// </summary>
/// <remarks>
/// Emoji flags are not an option here: Segoe UI Emoji carries no regional indicator glyphs, so a
/// flag emoji renders as its two letters in boxes. Drawing them keeps Windows and macOS identical
/// and needs no assets. Arabic is not a country, so it gets its script letter rather than a flag.
/// </remarks>
internal static class Flags
{
    private const double Width = 16;
    private const double Height = 11;

    public static FrameworkElement? For(string? code) => code switch
    {
        "en" => Clipped(UnitedStates()),
        "en-GB" => Clipped(UnitedKingdom()),
        "es" => Clipped(Horizontal((Rgb(0xC6, 0x0B, 0x1E), 3), (Rgb(0xFF, 0xC4, 0x00), 5), (Rgb(0xC6, 0x0B, 0x1E), 3))),
        "fr" => Clipped(Vertical(Rgb(0x00, 0x26, 0x54), Rgb(0xFF, 0xFF, 0xFF), Rgb(0xED, 0x29, 0x39))),
        "de" => Clipped(Horizontal((Rgb(0x00, 0x00, 0x00), 4), (Rgb(0xDD, 0x00, 0x00), 4), (Rgb(0xFF, 0xCE, 0x00), 3))),
        "ar" => Script("ع"),
        _ => null
    };

    private static Border Clipped(UIElement child) => new()
    {
        Width = Width,
        Height = Height,
        CornerRadius = new CornerRadius(2),
        BorderBrush = Chrome.Line,
        BorderThickness = new Thickness(0.5),
        Child = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), 2, 2), Children = { child } }
    };

    private static Border Script(string letter) => new()
    {
        Width = Width,
        Height = Height,
        CornerRadius = new CornerRadius(2),
        Background = Chrome.Well,
        BorderBrush = Chrome.ButtonEdge,
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = letter,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = Chrome.Slate400,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private static UIElement Horizontal(params (Brush Fill, double Height)[] bands)
    {
        var stack = new StackPanel { Width = Width };
        foreach (var (fill, height) in bands) stack.Children.Add(new Rectangle { Width = Width, Height = height, Fill = fill });
        return stack;
    }

    private static UIElement Vertical(params Brush[] bands)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Height = Height };
        foreach (var fill in bands) stack.Children.Add(new Rectangle { Width = Width / bands.Length, Height = Height, Fill = fill });
        return stack;
    }

    private static UIElement UnitedStates()
    {
        var canvas = new Canvas { Width = Width, Height = Height, Background = Brushes.White };
        for (var i = 0; i < 6; i++) Canvas.SetTop(Add(canvas, new Rectangle { Width = Width, Height = 0.95, Fill = Rgb(0xB2, 0x22, 0x34) }), i * 1.85);
        Add(canvas, new Rectangle { Width = 7, Height = 5.5, Fill = Rgb(0x3C, 0x3B, 0x6E) });
        return canvas;
    }

    private static UIElement UnitedKingdom()
    {
        var canvas = new Canvas { Width = Width, Height = Height, Background = Rgb(0x01, 0x21, 0x69) };
        var white = Brushes.White;
        var red = Rgb(0xC8, 0x10, 0x2E);
        Saltire(canvas, white, 1.8, 0, 34.5);
        Saltire(canvas, white, 1.8, Height, -34.5);
        Saltire(canvas, red, 0.8, 0.6, 34.5);
        Saltire(canvas, red, 0.8, Height - 0.6, -34.5);
        Place(canvas, new Rectangle { Width = Width, Height = 3.4, Fill = white }, 0, 3.8);
        Place(canvas, new Rectangle { Width = 3.4, Height = Height, Fill = white }, 6.3, 0);
        Place(canvas, new Rectangle { Width = Width, Height = 1.8, Fill = red }, 0, 4.6);
        Place(canvas, new Rectangle { Width = 1.8, Height = Height, Fill = red }, 7.1, 0);
        return canvas;
    }

    private static void Saltire(Canvas canvas, Brush fill, double thickness, double top, double degrees)
    {
        var bar = new Rectangle { Width = 21, Height = thickness, Fill = fill, RenderTransform = new RotateTransform(degrees) };
        Place(canvas, bar, 0, top);
    }

    private static UIElement Add(Canvas canvas, UIElement child)
    {
        canvas.Children.Add(child);
        return child;
    }

    private static void Place(Canvas canvas, UIElement child, double left, double top)
    {
        canvas.Children.Add(child);
        Canvas.SetLeft(child, left);
        Canvas.SetTop(child, top);
    }

    private static Brush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
