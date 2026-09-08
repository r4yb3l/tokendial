using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tokendial.App.Panel;

/// <summary>Text blocks in the panel's type scale, with tabular figures so numbers do not jitter.</summary>
public static class Text
{
    public static TextBlock Make(string text, double size, Brush brush, FontWeight? weight = null, bool display = false, TextAlignment align = TextAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = brush,
            FontFamily = display ? Theme.DisplayFont : Theme.Font,
            FontWeight = weight ?? FontWeights.Normal,
            TextAlignment = align,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            UseLayoutRounding = false
        };
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
        return block;
    }

    public static TextBlock Primary(string text, double size = 12, FontWeight? weight = null, TextAlignment align = TextAlignment.Left) => Make(text, size, Theme.TextPrimary, weight, align: align);
    public static TextBlock Secondary(string text, double size = 11, TextAlignment align = TextAlignment.Left) => Make(text, size, Theme.TextSecondary, align: align);
    public static TextBlock Disabled(string text, double size = 11, TextAlignment align = TextAlignment.Left) => Make(text, size, Theme.TextDisabled, align: align);

    public static Border Bar(double fraction, double height, Brush fill, double width = double.NaN)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Theme.Track, Width = width, ClipToBounds = true, UseLayoutRounding = false };
        var value = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill, HorizontalAlignment = HorizontalAlignment.Left, UseLayoutRounding = false };
        track.SizeChanged += (_, e) => value.Width = Math.Max(0, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        track.Child = value;
        return track;
    }

    public static Border Hairline(Thickness margin) => new() { Height = 1, Background = Theme.Hairline, Margin = margin };
}
