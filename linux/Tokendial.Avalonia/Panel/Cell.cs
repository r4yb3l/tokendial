using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.Model;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// One provider in the expanded capsule: dial with mark and percent, name, headline label, thin bars for
/// the other windows. Ported from the Cell nested in windows/Tokendial.App/Panel/PanelContent.cs, including
/// the margins - the mark sits above the number inside the ring, and eleven and twenty-five are what put
/// them there.
/// </summary>
public sealed class Cell
{
    private readonly Dial dial = new(Tokens.ExpandedDial, Tokens.ExpandedStroke);
    private readonly MarkView mark;
    private readonly TextBlock percent = Text.Make("", 15, Tokens.TextPrimary, FontWeight.SemiBold, TextAlignment.Center);
    private readonly TextBlock name = Text.Primary("", 11, FontWeight.SemiBold, TextAlignment.Center);
    private readonly TextBlock label = Text.Secondary("", 10, TextAlignment.Center);
    private readonly StackPanel bars = new() { Margin = new Thickness(18, 3, 18, 0) };

    public Cell(string id)
    {
        Id = id;
        mark = new MarkView(id, Tokens.ExpandedMark)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 11, 0, 0)
        };
        percent.VerticalAlignment = VerticalAlignment.Top;
        percent.Margin = new Thickness(0, 25, 0, 0);
        percent.HorizontalAlignment = HorizontalAlignment.Stretch;

        var dialHost = new Avalonia.Controls.Panel
        {
            Width = Tokens.ExpandedDial,
            Height = Tokens.ExpandedDial,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { dial, mark, percent }
        };

        name.Margin = new Thickness(2, 1, 2, 0);
        Root = new StackPanel { Width = Tokens.CellWidth, Children = { dialHost, name, label, bars } };
    }

    public string Id { get; }
    public StackPanel Root { get; }

    public void Apply(Tile tile)
    {
        dial.Hollow = !tile.HasReading;
        dial.Fill = Tokens.Of(tile.Band);
        dial.Fraction = tile.HasReading ? Math.Clamp(tile.Fraction ?? 0, 0, 1) : 0;

        percent.Text = tile.HasReading && tile.Fraction is double f ? $"{Math.Round(f * 100):0}%"
            : tile.HasReading && tile.Headline?.Count is int c ? $"{c}" : "–";
        percent.Foreground = tile.HasReading ? Tokens.TextPrimary : Tokens.TextDisabled;
        mark.Fill = tile.HasReading ? Tokens.TextSecondary : Tokens.TextDisabled;

        name.Text = tile.Name;
        name.Foreground = tile.HasReading ? Tokens.TextPrimary : Tokens.TextDisabled;
        label.Text = tile.HasReading ? tile.HeadlineLabel : tile.StatusLabel;

        bars.Children.Clear();
        if (!tile.HasReading) return;
        foreach (var window in tile.Secondary.Take(2))
        {
            if (window.UsedFraction is not double used) continue;
            var bar = Text.Bar(used, 3, Tokens.Of(Bands.Of(used)));
            bar.Margin = new Thickness(0, 2, 0, 0);
            bars.Children.Add(bar);
        }
    }
}
