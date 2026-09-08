using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>The compact row of mini dials and the expanded grid of cells. Dials are kept between updates so readings glide instead of jumping.</summary>
public sealed class PanelContent
{
    private readonly Dictionary<string, Dial> compactDials = new();
    private readonly Dictionary<string, MarkView> compactMarks = new();
    private readonly Dictionary<string, Cell> cells = new();
    private readonly List<string> order = new();

    public StackPanel CompactRow { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, UseLayoutRounding = false };
    public Grid Expanded { get; } = new() { UseLayoutRounding = false };
    private readonly StackPanel cellRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, UseLayoutRounding = false };
    private readonly TextBlock sessionsLine = Text.Secondary("", 11, TextAlignment.Center);
    private readonly Border mutedBadge = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Watch, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };

    public event Action<string>? CellClicked;

    public PanelContent()
    {
        Expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        Expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });
        Expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(cellRow, 1);
        Grid.SetRow(sessionsLine, 2);
        sessionsLine.Margin = new Thickness(Theme.ExpandedPadding, 2, Theme.ExpandedPadding, 0);
        sessionsLine.VerticalAlignment = VerticalAlignment.Top;
        Expanded.Children.Add(cellRow);
        Expanded.Children.Add(sessionsLine);
    }

    public int Count => order.Count;

    public IReadOnlyList<(string Id, FrameworkElement Element)> CellElements => order.Select(id => (id, (FrameworkElement)cells[id].Root)).ToList();

    public static double CompactWidth(int n) => n == 0 ? 2 * Theme.CompactPadding + 40 : 2 * Theme.CompactPadding + n * Theme.CompactDial + (n - 1) * Theme.CompactSpacing;
    public static double ExpandedWidth(int n) => Math.Max(2 * Theme.ExpandedPadding + Math.Max(n, 1) * Theme.CellWidth, Theme.CardWidth + 2 * Theme.ExpandedPadding);

    /// <summary>Forces every label to be built again in the current language.</summary>
    public void Relocalize() => order.Clear();

    public void Apply(PanelModel model, DateTimeOffset now, bool animate)
    {
        var ids = model.Tiles.Select(t => t.Id).ToList();
        if (!ids.SequenceEqual(order))
        {
            order.Clear();
            order.AddRange(ids);
            Rebuild(model);
        }
        for (var i = 0; i < model.Tiles.Count; i++)
        {
            var tile = model.Tiles[i];
            var fraction = tile.HasReading ? Math.Clamp(tile.Fraction ?? 0, 0, 1) : 0;
            var mini = compactDials[tile.Id];
            mini.Hollow = !tile.HasReading;
            mini.Fill = Theme.Of(tile.Band);
            Glide(mini, fraction, animate);
            compactMarks[tile.Id].Pulse(tile.Activity?.State == SessionState.Waiting, Theme.AmpleColor, Theme.TextDisabledColor, tile.HasReading ? Theme.TextSecondary : Theme.TextDisabled);
            cells[tile.Id].Apply(tile, fraction, animate, i);
        }
        mutedBadge.Visibility = model.Muted > 0 ? Visibility.Visible : Visibility.Collapsed;
        sessionsLine.Text = SessionsCopy(model.Sessions, now);
        sessionsLine.Foreground = model.AnyWaiting ? Theme.Waiting : Theme.TextSecondary;
    }

    private static void Glide(Dial dial, double fraction, bool animate)
    {
        if (animate) dial.AnimateTo(fraction);
        else { dial.BeginAnimation(Dial.FractionProperty, null); dial.Fraction = fraction; }
    }

    private void Rebuild(PanelModel model)
    {
        CompactRow.Children.Clear();
        cellRow.Children.Clear();
        compactDials.Clear();
        compactMarks.Clear();
        cells.Clear();
        if (model.Tiles.Count == 0)
        {
            CompactRow.Children.Add(Text.Disabled("Tokendial", 11));
            cellRow.Children.Add(new Border { Width = Theme.CellWidth * 2, Child = Text.Secondary(Strings.T("panel.noProviders"), 11, TextAlignment.Center), VerticalAlignment = VerticalAlignment.Center });
        }
        for (var i = 0; i < model.Tiles.Count; i++)
        {
            var tile = model.Tiles[i];
            var mini = new Dial(Theme.CompactDial, Theme.CompactStroke);
            var mark = new MarkView(tile.Id, Theme.CompactMark) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var host = new Grid { Width = Theme.CompactDial, Height = Theme.CompactDial, Margin = new Thickness(i == 0 ? 0 : Theme.CompactSpacing, 0, 0, 0) };
            host.Children.Add(mini);
            host.Children.Add(mark);
            compactDials[tile.Id] = mini;
            compactMarks[tile.Id] = mark;
            CompactRow.Children.Add(host);
            var cell = new Cell(tile.Id);
            cell.Root.MouseLeftButtonUp += (_, _) => CellClicked?.Invoke(tile.Id);
            cells[tile.Id] = cell;
            cellRow.Children.Add(cell.Root);
        }
        CompactRow.Children.Add(mutedBadge);
    }

    public static string SessionsCopy(IReadOnlyList<AgentSession> sessions, DateTimeOffset now)
    {
        if (sessions.Count == 0) return Strings.T("panel.noAgents");
        var waiting = sessions.Where(s => s.State == SessionState.Waiting).ToList();
        if (waiting.Count > 0)
        {
            var first = waiting[0];
            var rest = waiting.Count > 1 ? Strings.Plural("panel.waitingMore", waiting.Count - 1) : "";
            return Strings.T("panel.waiting", ("name", first.Name), ("elapsed", Copy.Elapsed(first.Since, now))) + rest;
        }
        var working = sessions.Where(s => s.State == SessionState.Working).ToList();
        if (working.Count == 1) return Strings.T("panel.workingOne", ("name", working[0].Name), ("elapsed", Copy.Elapsed(working[0].Since, now)));
        if (working.Count > 1) return Strings.T("panel.workingMany", ("n", working.Count), ("names", string.Join(", ", working.Take(3).Select(s => s.Name))));
        return Strings.Plural("panel.idle", sessions.Count);
    }

    /// <summary>One provider in the expanded capsule: dial with mark and percent, name, headline label, thin bars for the other windows.</summary>
    private sealed class Cell
    {
        private readonly Dial dial = new(Theme.ExpandedDial, Theme.ExpandedStroke);
        private readonly ActivityArc activity = new();
        private readonly MarkView mark;
        private readonly TextBlock percent = Text.Make("", 15, Theme.TextPrimary, FontWeights.SemiBold, display: true, align: TextAlignment.Center);
        private readonly TextBlock name = Text.Primary("", 11, FontWeights.SemiBold, TextAlignment.Center);
        private readonly TextBlock label = Text.Secondary("", 10, TextAlignment.Center);
        private readonly StackPanel bars = new() { Margin = new Thickness(18, 3, 18, 0) };
        private bool shown;

        public Cell(string id)
        {
            Id = id;
            mark = new MarkView(id, Theme.ExpandedMark) { HorizontalAlignment = HorizontalAlignment.Center };
            percent.FlowDirection = FlowDirection.LeftToRight;
            var stack = new StackPanel { Width = Theme.CellWidth, UseLayoutRounding = false };
            var dialHost = new Grid { Width = Theme.ExpandedDial, Height = Theme.ExpandedDial, HorizontalAlignment = HorizontalAlignment.Center };
            dialHost.Children.Add(dial);
            activity.HorizontalAlignment = HorizontalAlignment.Center;
            activity.VerticalAlignment = VerticalAlignment.Center;
            dialHost.Children.Add(activity);
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 11, 0, 0);
            percent.VerticalAlignment = VerticalAlignment.Top;
            percent.Margin = new Thickness(0, 25, 0, 0);
            dialHost.Children.Add(mark);
            dialHost.Children.Add(percent);
            stack.Children.Add(dialHost);
            name.Margin = new Thickness(2, 1, 2, 0);
            stack.Children.Add(name);
            stack.Children.Add(label);
            stack.Children.Add(bars);
            Root = new Border { Child = stack, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Opacity = 0, UseLayoutRounding = false };
        }

        public string Id { get; }
        public Border Root { get; }

        public void Apply(Tile tile, double fraction, bool animate, int index)
        {
            dial.Hollow = !tile.HasReading;
            dial.Fill = Theme.Of(tile.Band);
            if (animate) dial.AnimateTo(fraction); else { dial.BeginAnimation(Dial.FractionProperty, null); dial.Fraction = fraction; }
            percent.Text = tile.HasReading && tile.Fraction is double f ? $"{Math.Round(f * 100):0}%" : tile.HasReading && tile.Headline?.Count is int c ? $"{c}" : "–";
            percent.Foreground = tile.HasReading ? Theme.TextPrimary : Theme.TextDisabled;
            mark.Pulse(tile.Activity?.State == SessionState.Waiting, Theme.AmpleColor, Theme.TextDisabledColor, tile.HasReading ? Theme.TextSecondary : Theme.TextDisabled);
            name.Text = tile.Name;
            name.Foreground = tile.HasReading ? Theme.TextPrimary : Theme.TextDisabled;
            label.Text = tile.HasReading ? tile.HeadlineLabel : tile.StatusLabel;
            bars.Children.Clear();
            if (tile.HasReading)
            {
                foreach (var window in tile.Secondary.Take(2))
                {
                    if (window.UsedFraction is not double used) continue;
                    var bar = Text.Bar(used, 3, Theme.Of(Bands.Of(used)));
                    bar.Margin = new Thickness(0, 2, 0, 0);
                    bars.Children.Add(bar);
                }
            }
            activity.Show(tile.Activity?.State);
            if (!shown)
            {
                shown = true;
                var duration = Theme.DurationOf(Theme.Contents);
                if (duration.TimeSpan == TimeSpan.Zero) Root.Opacity = 1;
                else Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = TimeSpan.FromSeconds(Theme.Stagger(index)), EasingFunction = Theme.Contents });
            }
        }
    }
}
