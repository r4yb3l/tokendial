using System.Windows;
using System.Windows.Controls;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>The detail card under a hovered cell: every window with its reset copy, a bar, both ends of the number, then the live sessions.</summary>
public static class HoverCard
{
    public static Border Build(Tile tile, DateTimeOffset now)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var mark = new MarkView(tile.Id, 16) { Fill = Theme.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(mark, Dock.Left);
        header.Children.Add(mark);
        var title = Text.Primary(tile.Name, 14, FontWeights.SemiBold);
        var account = tile.Account?.Summary;
        if (!string.IsNullOrEmpty(account))
        {
            var right = Text.Secondary(account, 11, TextAlignment.Right);
            right.MaxWidth = 130;
            right.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(right, Dock.Right);
            header.Children.Add(right);
        }
        header.Children.Add(title);
        body.Children.Add(header);

        switch (tile.Reading.Status)
        {
            case ReadingStatus.NeedsSignIn:
                body.Children.Add(Wrap(Text.Secondary("Not signed in.", 12)));
                break;
            case ReadingStatus.Unsupported u:
                body.Children.Add(Wrap(Text.Secondary(u.Why, 12)));
                break;
            case ReadingStatus.Failed f:
                body.Children.Add(Wrap(Text.Secondary($"Could not read usage ({f.Why}). Showing nothing rather than a guess.", 12)));
                break;
        }

        foreach (var window in tile.Reading.Windows)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) };
            var reset = window.ResetsAt is DateTimeOffset at ? Copy.Reset(at, now) : "";
            var resetBlock = Text.Secondary(reset, 11, TextAlignment.Right);
            resetBlock.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(resetBlock, Dock.Right);
            row.Children.Add(resetBlock);
            row.Children.Add(Text.Primary(window.Label, 12));
            body.Children.Add(row);
            if (window.UsedFraction is double fraction)
            {
                body.Children.Add(WithMargin(Text.Bar(fraction, 4, Theme.Of(tile.Reading.Block is not null ? Band.Critical : Bands.Of(fraction))), new Thickness(0, 5, 0, 4)));
            }
            body.Children.Add(Text.Secondary(window.Summary(tile.Reading.Fidelity), 11));
        }

        if (tile.Reading.Block is Blocked block)
        {
            body.Children.Add(WithMargin(Text.Make(Copy.Until(block.Reason, block.Until, now), 11, Theme.Critical), new Thickness(0, 8, 0, 0)));
        }

        if (tile.Reading.Status is ReadingStatus.Stale stale && stale.Since > DateTimeOffset.MinValue)
        {
            body.Children.Add(WithMargin(Text.Disabled($"Last read {Copy.Ago(stale.Since, now)}", 11), new Thickness(0, 8, 0, 0)));
        }
        if (tile.Reading.Fidelity == Fidelity.Derived)
        {
            body.Children.Add(WithMargin(Text.Disabled("Derived from local activity, not published by the provider", 11), new Thickness(0, 6, 0, 0)));
        }

        if (tile.Activity is Activity activity && activity.Sessions.Count > 0)
        {
            body.Children.Add(Text.Hairline(new Thickness(0, 10, 0, 8)));
            foreach (var session in activity.Ordered.Take(4)) body.Children.Add(SessionRow(session, now));
        }

        return new Border
        {
            Width = Theme.CardWidth,
            Background = Theme.CardSurface,
            BorderBrush = Theme.SurfaceEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.CardRadius),
            Padding = new Thickness(Theme.CardPadding, Theme.CardPadding, Theme.CardPadding + 2, Theme.CardPadding),
            Child = body,
            UseLayoutRounding = false
        };
    }

    public static DockPanel SessionRow(AgentSession session, DateTimeOffset now)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = session.State == SessionState.Waiting ? Theme.Waiting : session.State == SessionState.Working ? Theme.Working : Theme.TextDisabled };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        var when = Text.Secondary(session.State == SessionState.Waiting ? $"waiting {Copy.Elapsed(session.Since, now)}" : Copy.Elapsed(session.Since, now), 11, TextAlignment.Right);
        when.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(when, Dock.Right);
        row.Children.Add(when);
        var label = session.WaitingFor is string why && session.State == SessionState.Waiting ? $"{session.Name} · {why}" : $"{session.Name} · {session.Where}";
        row.Children.Add(Text.Primary(label, 12));
        return row;
    }

    private static TextBlock Wrap(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    private static T WithMargin<T>(T element, Thickness margin) where T : FrameworkElement
    {
        element.Margin = margin;
        return element;
    }
}
