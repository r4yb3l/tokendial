using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The detail card under a hovered cell: every window with its reset copy, a bar, both ends of the number,
/// then the live sessions. Ported from windows/Tokendial.App/Panel/HoverCard.cs - the copy keys and the
/// order of the rows are the product, and are not re-decided per platform.
/// </summary>
public static class HoverCard
{
    public static Border Build(Tile tile, DateTimeOffset now)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var mark = new MarkView(tile.Id, 16) { Fill = Tokens.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(mark, Dock.Left);
        header.Children.Add(mark);
        if (tile.Account?.Summary is string account && account.Length > 0)
        {
            var right = Text.Secondary(account, 11, TextAlignment.Right);
            right.MaxWidth = 130;
            right.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(right, Dock.Right);
            header.Children.Add(right);
        }
        header.Children.Add(Text.Primary(tile.Name, 14, FontWeight.SemiBold));
        body.Children.Add(header);

        switch (tile.Reading.Status)
        {
            case ReadingStatus.NeedsSignIn:
                body.Children.Add(Text.Wrap(Text.Secondary(Strings.T("card.notSignedIn"), 12)));
                break;
            case ReadingStatus.Unsupported u:
                body.Children.Add(Text.Wrap(Text.Secondary(u.Why, 12)));
                break;
            case ReadingStatus.Failed f:
                body.Children.Add(Text.Wrap(Text.Secondary(Strings.T("card.failed", ("why", f.Why)), 12)));
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
            row.Children.Add(Text.Primary(Strings.Label(window.Label), 12));
            body.Children.Add(row);

            if (window.UsedFraction is double fraction)
            {
                var band = tile.Reading.Block is not null ? Band.Critical : Bands.Of(fraction);
                body.Children.Add(Text.WithMargin(Text.Bar(fraction, 4, Tokens.Of(band)), new Thickness(0, 5, 0, 4)));
            }
            body.Children.Add(Text.Secondary(window.Summary(tile.Reading.Fidelity), 11));

            if (tile.Forecast is Forecast forecast && window.Id == tile.Headline?.Id)
            {
                var pace = Text.Make(Copy.Forecast(forecast, now), 11,
                    forecast.Kind == ForecastKind.RunsOut ? Tokens.Watch : Tokens.TextSecondary);
                body.Children.Add(Text.WithMargin(Text.Wrap(pace), new Thickness(0, 2, 0, 0)));
            }
        }

        if (tile.Reading.Block is Blocked block)
            body.Children.Add(Text.WithMargin(Text.Make(Copy.Until(block.Reason, block.Until, now), 11, Tokens.Critical), new Thickness(0, 8, 0, 0)));

        if (tile.Reading.Status is ReadingStatus.Stale stale && stale.Since > DateTimeOffset.MinValue)
            body.Children.Add(Text.WithMargin(Text.Disabled(Strings.T("card.lastRead", ("ago", Copy.Ago(stale.Since, now))), 11), new Thickness(0, 8, 0, 0)));

        if (tile.Reading.Fidelity == Fidelity.Derived)
            body.Children.Add(Text.WithMargin(Text.Disabled(Strings.T("card.derived"), 11), new Thickness(0, 6, 0, 0)));

        if (tile.Activity is Activity activity && activity.Sessions.Count > 0)
        {
            body.Children.Add(Text.Hairline(new Thickness(0, 10, 0, 8)));
            foreach (var session in activity.Ordered.Take(4)) body.Children.Add(SessionRow(session, now));
        }

        return new Border
        {
            Width = Tokens.CardWidth,
            Background = Tokens.CardSurface,
            BorderBrush = Tokens.SurfaceEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.CardRadius),
            Padding = new Thickness(Tokens.CardPadding, Tokens.CardPadding, Tokens.CardPadding + 2, Tokens.CardPadding),
            Child = body
        };
    }

    private static DockPanel SessionRow(AgentSession session, DateTimeOffset now)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
        var dot = new Border
        {
            Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = session.State == SessionState.Waiting ? Tokens.Waiting
                : session.State == SessionState.Working ? Tokens.Working : Tokens.TextDisabled
        };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);

        var when = Text.Secondary(session.State == SessionState.Waiting
            ? Strings.T("card.waiting", ("elapsed", Copy.Elapsed(session.Since, now)))
            : Copy.Elapsed(session.Since, now), 11, TextAlignment.Right);
        when.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(when, Dock.Right);
        row.Children.Add(when);

        var label = session.WaitingFor is string why && session.State == SessionState.Waiting
            ? $"{session.Name} · {why}"
            : $"{session.Name} · {session.Where}";
        row.Children.Add(Text.Primary(label, 12));
        return row;
    }
}
