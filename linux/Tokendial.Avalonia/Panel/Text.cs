using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>Text blocks in the panel's type scale. The Avalonia twin of windows/Tokendial.App/Panel/Text.cs.</summary>
public static class Text
{
    public static TextBlock Make(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal,
        TextAlignment align = TextAlignment.Left)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = brush,
            FontFamily = Tokens.Font,
            FontWeight = weight,
            TextAlignment = align,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

    public static TextBlock Primary(string text, double size = 12, FontWeight weight = FontWeight.Normal, TextAlignment align = TextAlignment.Left)
        => Make(text, size, Tokens.TextPrimary, weight, align);

    public static TextBlock Secondary(string text, double size = 11, TextAlignment align = TextAlignment.Left)
        => Make(text, size, Tokens.TextSecondary, align: align);

    public static TextBlock Disabled(string text, double size = 11, TextAlignment align = TextAlignment.Left)
        => Make(text, size, Tokens.TextDisabled, align: align);

    /// <summary>A rounded track with the used part filled. The value width follows the track's, so it works at any width.</summary>
    public static Border Bar(double fraction, double height, IBrush fill)
    {
        var value = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = fill,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var track = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = Tokens.Track,
            ClipToBounds = true,
            Child = value
        };
        track.SizeChanged += (_, e) => value.Width = Math.Max(0, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        return track;
    }

    public static Border Hairline(Thickness margin) => new() { Height = 1, Background = Tokens.Hairline, Margin = margin };

    public static T Wrap<T>(T block) where T : TextBlock
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    public static T WithMargin<T>(T element, Thickness margin) where T : Control
    {
        element.Margin = margin;
        return element;
    }
}
