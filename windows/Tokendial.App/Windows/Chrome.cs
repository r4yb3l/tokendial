using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Tokendial.App.Panel;

namespace Tokendial.App.Windows;

/// <summary>Dark, plain controls for the settings and welcome windows, built in code so they share the panel's palette.</summary>
public static class Chrome
{
    public static readonly Brush WindowBackground = Frozen(Color.FromRgb(0x14, 0x15, 0x19));
    public static readonly Brush Panel = Frozen(Color.FromRgb(0x1C, 0x1E, 0x24));
    public static readonly Brush Control = Frozen(Color.FromRgb(0x26, 0x28, 0x30));
    public static readonly Brush ControlHover = Frozen(Color.FromRgb(0x30, 0x33, 0x3C));
    public static readonly Brush Accent = Theme.Ample;

    /// <summary>A window with its own dark title bar: the standard frame stays underneath, so dragging, resizing, snapping and rounded corners keep working.</summary>
    public static Window Frame(string title, double width, double height, UIElement content)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 420,
            MinHeight = 320,
            Background = WindowBackground,
            Foreground = Theme.TextPrimary,
            FontFamily = Theme.Font,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            ResizeMode = ResizeMode.CanResize,
            SnapsToDevicePixels = true,
            WindowStyle = WindowStyle.SingleBorderWindow
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = TitleBarHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = TitleBar(window);
        root.Children.Add(bar);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        window.Content = root;
        window.StateChanged += (_, _) => root.Margin = window.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        return window;
    }

    public const double TitleBarHeight = 40;

    private static Grid TitleBar(Window window)
    {
        var bar = new Grid { Background = WindowBackground };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dial = new Dial(16, 2.5) { Fill = Accent, Fraction = 0.66, Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(dial);
        var title = Text.Make(window.Title, 12, Theme.TextSecondary, FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);
        window.SetBinding(Window.TitleProperty, new System.Windows.Data.Binding("Text") { Source = title, Mode = System.Windows.Data.BindingMode.OneWay });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 2);
        buttons.Children.Add(CaptionButton("", () => window.WindowState = WindowState.Minimized, false));
        var maximize = CaptionButton("", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false);
        window.StateChanged += (_, _) => ((TextBlock)maximize.Content).Text = window.WindowState == WindowState.Maximized ? "" : "";
        buttons.Children.Add(maximize);
        buttons.Children.Add(CaptionButton("", window.Close, true));
        bar.Children.Add(buttons);
        bar.FlowDirection = FlowDirection.LeftToRight;
        return bar;
    }

    private static Button CaptionButton(string glyph, Action click, bool danger)
    {
        var button = new Button
        {
            Width = 46,
            Height = TitleBarHeight,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.TextSecondary,
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Focusable = false
        };
        button.Template = CaptionTemplate(danger);
        button.Click += (_, _) => click();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        return button;
    }

    private static ControlTemplate CaptionTemplate(bool danger)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "bg");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, danger ? Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C)) : ControlHover, "bg"));
        if (danger) hover.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, Brushes.White));
        template.Triggers.Add(hover);
        return template;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0)
    };

    public static TextBlock Heading(string text) => Text.Make(text, 13, Theme.TextPrimary, FontWeights.SemiBold, display: true);
    public static TextBlock Body(string text, bool wrap = true)
    {
        var block = Text.Make(text, 12, Theme.TextSecondary);
        if (wrap) { block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.None; }
        return block;
    }
    public static TextBlock Label(string text) => Text.Make(text, 12, Theme.TextPrimary);

    public static Border Section(string title, string? description, UIElement body)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title));
        if (description is not null)
        {
            var d = Body(description);
            d.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(d);
        }
        if (body is FrameworkElement fe) fe.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(body);
        return new Border { Background = Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 16), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    public static CheckBox Check(string label, bool value, Action<bool> changed, string? hint = null)
    {
        var box = new CheckBox { IsChecked = value, Foreground = Theme.TextPrimary, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        var stack = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        stack.Children.Add(Label(label));
        if (hint is not null) { var h = Body(hint); h.FontSize = 11; stack.Children.Add(h); }
        box.Content = stack;
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    public static RadioButton Radio(string group, string label, bool value, Action changed, string? hint = null)
    {
        var button = new RadioButton { GroupName = group, IsChecked = value, Foreground = Theme.TextPrimary, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
        var stack = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        stack.Children.Add(Label(label));
        if (hint is not null) { var h = Body(hint); h.FontSize = 11; stack.Children.Add(h); }
        button.Content = stack;
        button.Checked += (_, _) => changed();
        return button;
    }

    public static Button Button(string label, Action click, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = primary ? Accent : Control,
            Foreground = primary ? Frozen(Color.FromRgb(0x0B, 0x1F, 0x17)) : Theme.TextPrimary,
            BorderThickness = new Thickness(0),
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Template = FlatButtonTemplate();
        button.Click += (_, _) => click();
        return button;
    }

    public static TextBox Field(string value, Action<string> changed, double width = 120)
    {
        var box = new TextBox
        {
            Text = value,
            Width = width,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Control,
            Foreground = Theme.TextPrimary,
            BorderBrush = Theme.Hairline,
            BorderThickness = new Thickness(1),
            CaretBrush = Theme.TextPrimary,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        box.LostFocus += (_, _) => changed(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) changed(box.Text); };
        return box;
    }

    public static Grid Row(string label, UIElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Label(label));
        if (hint is not null) { var h = Body(hint); h.FontSize = 11; stack.Children.Add(h); }
        grid.Children.Add(stack);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    private static ControlTemplate FlatButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88));
        template.Triggers.Add(hover);
        return template;
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
