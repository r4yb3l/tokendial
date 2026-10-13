using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Tokendial.App.Interop;
using Tokendial.Core.I18n;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>
/// What the face says while an agent waits on you: who is waiting, where, and a way to get there. It replaces the
/// waiting banner while the face is on, and goes away by itself once nobody is waiting. Drawn in the face's own
/// dark palette, since it is the face talking.
/// </summary>
public sealed class SpeechBubble : Window
{
    private const int MaxRows = 3;
    private const double TailSize = 8;

    private static readonly Theme.Palette Screen = Theme.DarkPalette;

    private readonly StackPanel rows = new();
    private readonly Polygon tail = new() { Points = [new(TailSize, 0), new(0, TailSize), new(TailSize, TailSize * 2)], Fill = Theme.FaceGround, VerticalAlignment = VerticalAlignment.Top };
    private readonly HashSet<string> settled = [];
    private IReadOnlyList<(AgentSession Session, string Name)> heard = [];
    private IReadOnlyList<(AgentSession Session, string Name)> said = [];

    public SpeechBubble()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Tokendial";
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;

        var card = new Border
        {
            Background = Theme.FaceGround,
            BorderBrush = Screen.SurfaceEdge,
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
        SourceInitialized += (_, _) => Native.MakeUnobtrusive(new WindowInteropHelper(this).Handle);
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
            tail.RenderTransformOrigin = new Point(0.5, 0.5);
            tail.RenderTransform = value ? Transform.Identity : new ScaleTransform(-1, 1);
            tail.Margin = value ? new Thickness(0, 32, -1, 0) : new Thickness(-1, 32, 0, 0);
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

    private FrameworkElement Row(AgentSession session, string name, bool first)
    {
        var row = new StackPanel { Margin = new Thickness(0, first ? 0 : 12, 0, 0) };
        var title = Text.Make(Strings.T("alert.waiting.title", ("name", name)), 13, Screen.TextPrimary, FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(title);
        var where = Text.Make(session.WaitingFor is string what ? $"{session.Where} · {what}" : session.Where, 11.5, Screen.TextSecondary);
        where.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(where);
        if (session.Pid is int pid) row.Children.Add(TakeMeThere(session.Id, pid));
        return row;
    }

    private FrameworkElement TakeMeThere(string sessionId, int pid)
    {
        var label = Text.Make(Strings.T("mascot.takeMeThere"), 12, Screen.TextPrimary, FontWeights.Medium);
        var button = new Border
        {
            Background = Screen.Track,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 6),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            Child = label
        };
        button.MouseEnter += (_, _) => button.Background = Screen.TextDisabled;
        button.MouseLeave += (_, _) => button.Background = Screen.Track;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (Native.BringForward(pid))
            {
                settled.Add(sessionId);
                Say(heard);
                return;
            }
            label.Text = Strings.T("mascot.notFound");
            label.Foreground = Screen.TextSecondary;
        };
        return button;
    }
}
