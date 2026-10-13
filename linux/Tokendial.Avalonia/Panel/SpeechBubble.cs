using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.I18n;
using Tokendial.Core.Sessions;
using Tokendial.Linux.Interop;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// What the face says while an agent waits on you: who is waiting, where, and a way to get there. It replaces the
/// waiting notification while the face is on and goes away by itself once nobody is waiting. The Avalonia twin of
/// windows/Tokendial.App/Panel/SpeechBubble.cs, in the face's own dark colours, since it is the face talking.
/// </summary>
public sealed class SpeechBubble : Window
{
    private const int MaxRows = 3;
    private const double TailSize = 8;
    private const double TailTop = 32;

    private readonly StackPanel rows = new();
    private readonly Polygon tail = new()
    {
        Points = [new Point(TailSize, 0), new Point(0, TailSize), new Point(TailSize, TailSize * 2)],
        Fill = Tokens.FaceGround,
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly HashSet<string> settled = [];
    private IReadOnlyList<(AgentSession Session, string Name)> heard = [];
    private IReadOnlyList<(AgentSession Session, string Name)> said = [];

    public SpeechBubble()
    {
        Title = "Tokendial says";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var card = new Border
        {
            Background = Tokens.FaceGround,
            BorderBrush = Tokens.SurfaceEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            MaxWidth = 300,
            Child = rows
        };
        var layout = new DockPanel();
        layout.Children.Add(tail);
        layout.Children.Add(card);
        Content = layout;
        FaceOnLeft = true;
        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle();
            if (handle is not { HandleDescriptor: "XID", Handle: not 0 } || !X11.Open()) return;
            X11.MakeDock(handle.Handle);
            X11.RefuseFocus(handle.Handle);
        };
    }

    public bool HasSomethingToSay => said.Count > 0;

    /// <summary>What the bubble says changed, so it may need moving or hiding.</summary>
    public event Action? Changed;

    /// <summary>Points the tail at the face, which sits on the left of the bubble or on its right.</summary>
    public bool FaceOnLeft
    {
        set
        {
            DockPanel.SetDock(tail, value ? Dock.Left : Dock.Right);
            tail.RenderTransform = value ? null : new ScaleTransform(-1, 1);
            tail.Margin = value ? new Thickness(0, TailTop, -1, 0) : new Thickness(-1, TailTop, 0, 0);
        }
    }

    /// <summary>
    /// Says who is waiting, leaving out a session the user was already taken to until it stops waiting and starts
    /// again. Rebuilds only when that list changes, so a refresh never resets a "not found" the user is reading.
    /// </summary>
    public void Say(IReadOnlyList<(AgentSession Session, string Name)> waiting)
    {
        heard = waiting;
        settled.IntersectWith(waiting.Select(w => w.Session.Id));
        var saying = waiting.Where(w => !settled.Contains(w.Session.Id)).ToList();
        if (saying.Select(w => w.Session.Id).SequenceEqual(said.Select(w => w.Session.Id))) return;
        said = saying;
        FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        rows.Children.Clear();
        foreach (var (session, name) in saying.Take(MaxRows)) rows.Children.Add(Row(session, name, first: rows.Children.Count == 0));
        Changed?.Invoke();
    }

    private Control Row(AgentSession session, string name, bool first)
    {
        var row = new StackPanel { Margin = new Thickness(0, first ? 0 : 12, 0, 0) };
        var title = Text.Make(Strings.T("alert.waiting.title", ("name", name)), 13, Tokens.TextPrimary, FontWeight.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(title);
        var where = Text.Make(session.WaitingFor is string what ? $"{session.Where} · {what}" : session.Where, 11.5, Tokens.TextSecondary);
        where.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(where);
        if (session.Pid is int pid) row.Children.Add(TakeMeThere(session.Id, pid));
        return row;
    }

    private Control TakeMeThere(string sessionId, int pid)
    {
        var label = Text.Make(Strings.T("mascot.takeMeThere"), 12, Tokens.TextPrimary, FontWeight.Medium);
        var button = new Border
        {
            Background = Tokens.Track,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 6),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label
        };
        button.PointerEntered += (_, _) => button.Background = Tokens.TextDisabled;
        button.PointerExited += (_, _) => button.Background = Tokens.Track;
        button.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            if (X11.ActivateOwnerOf(pid))
            {
                settled.Add(sessionId);
                Say(heard);
                return;
            }
            label.Text = Strings.T("mascot.notFound");
            label.Foreground = Tokens.TextSecondary;
        };
        return button;
    }
}
