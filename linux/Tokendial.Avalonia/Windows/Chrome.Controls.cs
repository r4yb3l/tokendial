using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Tokendial.Linux.Windows;

/// <summary>
/// The interactive half of the vocabulary. WPF builds these out of FrameworkElementFactory and Triggers -
/// roughly 350 lines of it - because that is the only way to write a ControlTemplate in code there. Avalonia
/// has pseudo-class selectors, so the same eight controls are shorter and read as rules rather than as trees.
/// The signatures stay identical to the WPF ones so their callers do not notice.
/// </summary>
public static partial class Chrome
{
    /// <summary>
    /// State is applied by hand rather than through pseudo-class styles. A Style added to a control's own
    /// Styles collection reaches that control's descendants, not the control itself, so `:checked` selectors
    /// on a toggle never matched and every switch in settings drew as off - including the ones that were on.
    /// Building the parts outside the template lets the handler simply move them.
    /// </summary>
    private static void Paint(Border track, Border knob, bool on)
    {
        track.Background = on ? Accent : Surface700;
        track.BorderBrush = on ? Accent : ButtonEdge;
        knob.Background = on ? Brushes.White : Slate300;
        knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        knob.Margin = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    // ---- switch ------------------------------------------------------------------------------------

    public static ToggleButton Switch(bool value, Action<bool> changed)
    {
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Child = knob };
        Paint(track, knob, value);

        var toggle = new ToggleButton
        {
            IsChecked = value, Width = 40, Height = 22, Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Template = new FuncControlTemplate<ToggleButton>((_, _) => track)
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            Paint(track, knob, on);
            changed(on);
        };
        return toggle;
    }

    public static Control SwitchRow(string label, string? hint, bool value, Action<bool> changed) =>
        Row(label, Switch(value, changed), hint);

    // ---- check -------------------------------------------------------------------------------------

    public static CheckBox CheckBox(bool value, Action<bool> changed)
    {
        var tick = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 2,6 L 5,9 L 10,3"), Stroke = Brushes.White, StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var box = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = tick };
        void Show(bool on)
        {
            box.Background = on ? Accent : Surface750;
            box.BorderBrush = on ? Accent : ButtonEdge;
            tick.Opacity = on ? 1 : 0;
        }
        Show(value);

        var check = new CheckBox
        {
            IsChecked = value, Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
            Template = new FuncControlTemplate<CheckBox>((_, _) => box)
        };
        check.IsCheckedChanged += (_, _) => { var on = check.IsChecked == true; Show(on); changed(on); };
        return check;
    }

    public static CheckBox Check(string label, bool value, Action<bool> changed, string? hint = null)
    {
        var inner = CheckBox(value, changed);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Label(label) } };
        if (hint is not null) text.Children.Add(Body(hint));

        var built = (Control)((FuncControlTemplate<CheckBox>)inner.Template!).Build(inner)!.Result!;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { built, text }
        };
        inner.Template = new FuncControlTemplate<CheckBox>((_, _) => row);
        return inner;
    }

    public static CheckBox CheckChip(string label, bool value) => Check(label, value, _ => { });


    // ---- radio -------------------------------------------------------------------------------------

    public static RadioButton Radio(string group, string label, bool value, Action changed, string? hint = null)
    {
        var radio = Bare(group, value, changed);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Label(label) } };
        if (hint is not null) text.Children.Add(Body(hint));
        radio.Content = text;
        radio.ContentTemplate = null;
        return Dress(radio, dot: true);
    }

    public static RadioButton RadioCard(string group, string label, bool value, Action changed, string? hint = null)
    {
        var radio = Radio(group, label, value, changed, hint);
        radio.Margin = new Thickness(0, 4, 0, 0);
        return radio;
    }

    public static RadioButton Chip(string group, object content, bool value, Action changed)
    {
        var radio = Bare(group, value, changed);
        radio.Content = content as Control ?? Label(content?.ToString() ?? "");
        return Dress(radio, dot: false, radius: 999, padding: new Thickness(12, 5, 12, 5));
    }

    public static RadioButton Tile(string group, Control diagram, string label, bool value, Action changed)
    {
        var radio = Bare(group, value, changed);
        radio.Content = new StackPanel { Spacing = 8, Children = { diagram, Label(label) } };
        return Dress(radio, dot: false, radius: 12, padding: new Thickness(12));
    }

    private static RadioButton Bare(string group, bool value, Action changed)
    {
        var radio = new RadioButton { GroupName = group, IsChecked = value, Cursor = new Cursor(StandardCursorType.Hand) };
        radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) changed(); };
        return radio;
    }
    /// <summary>
    /// A radio drawn as a surface that lights up when chosen, with an optional dot on its left. Painted by
    /// hand for the same reason the switch is: a control's own Styles do not reach the control.
    /// </summary>
    private static RadioButton Dress(RadioButton radio, bool dot, double radius = 10, Thickness? padding = null)
    {
        var pad = padding ?? new Thickness(12, 10, 12, 10);
        var inner = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.White,
                                 HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ring = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                                VerticalAlignment = VerticalAlignment.Center, Child = inner };
        var surface = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Padding = pad };

        void Show(bool on)
        {
            surface.Background = on ? BrandFaint : Well;
            surface.BorderBrush = on ? BrandSoft : EdgeSoft;
            ring.Background = on ? Accent : Surface750;
            ring.BorderBrush = on ? Accent : ButtonEdge;
            inner.Opacity = on ? 1 : 0;
        }
        Show(radio.IsChecked == true);

        var content = radio.Content as Control ?? Label(radio.Content?.ToString() ?? "");
        content.VerticalAlignment = VerticalAlignment.Center;
        radio.Content = null;
        surface.Child = dot
            ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { ring, content } }
            : content;
        radio.Template = new FuncControlTemplate<RadioButton>((_, _) => surface);

        radio.IsCheckedChanged += (_, _) => Show(radio.IsChecked == true);
        return radio;
    }

    // ---- buttons -----------------------------------------------------------------------------------

    public static Button Button(string label, Action click, bool primary = false)
    {
        var content = Panel.Text.Make(label, 12, primary ? Brushes.Black : Slate200, FontWeight.Medium);
        var surface = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = primary ? Accent : Surface750,
            BorderBrush = primary ? Accent : ButtonEdge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 7, 14, 7),
            Child = content
        };
        var button = new Button
        {
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            // The content is placed directly rather than through a ContentPresenter: the template hands back
            // an instance built once, and a presenter would try to re-parent a control that already has one.
            Template = new FuncControlTemplate<Button>((_, _) => surface)
        };
        button.PointerEntered += (_, _) => { if (!primary) surface.Background = Surface700; };
        button.PointerExited += (_, _) => { if (!primary) surface.Background = Surface750; };
        button.Click += (_, _) => click();
        return button;
    }


    public static Button WideButton(string icon, string label, Action click)
    {
        var button = Button(label, click);
        if (button.Template is FuncControlTemplate<Button> && button.GetValue(Avalonia.Controls.Button.TemplateProperty) is not null)
        {
            // Replace the label with icon-and-label in the surface already built for it.
            var surface = (Border)((FuncControlTemplate<Button>)button.Template!).Build(button)!.Result!;
            surface.Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { Icon(icon, 16, Slate300), Panel.Text.Make(label, 12, Slate200, FontWeight.Medium) }
            };
        }
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        return button;
    }

    // ---- the little illustrations the panel settings use --------------------------------------------

    public static Control CompactDiagram(bool wide)
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 3; i++)
            dots.Children.Add(new Border { Width = wide ? 10 : 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Slate400 });
        return Screen(dots, top: true);
    }

    public static Control TrayDiagram() =>
        Screen(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Slate400, HorizontalAlignment = HorizontalAlignment.Center }, top: true);

    public static Control EdgeDiagram(bool vertical, bool far, bool selected)
    {
        var bar = new Border
        {
            Background = selected ? Accent : Slate400,
            CornerRadius = new CornerRadius(2),
            Width = vertical ? 4 : 26,
            Height = vertical ? 22 : 4,
            HorizontalAlignment = vertical ? (far ? HorizontalAlignment.Right : HorizontalAlignment.Left) : HorizontalAlignment.Center,
            VerticalAlignment = vertical ? VerticalAlignment.Center : (far ? VerticalAlignment.Bottom : VerticalAlignment.Top),
            Margin = new Thickness(3)
        };
        return Screen(bar, top: false);
    }

    private static Control Screen(Control child, bool top) => new Border
    {
        Width = 54, Height = 34, CornerRadius = new CornerRadius(6), Background = Surface800,
        BorderBrush = EdgeSoft, BorderThickness = new Thickness(1),
        Padding = new Thickness(4, top ? 4 : 0, 4, 0),
        Child = new Avalonia.Controls.Panel
        {
            Children = { child },
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Stretch
        }
    };
}
