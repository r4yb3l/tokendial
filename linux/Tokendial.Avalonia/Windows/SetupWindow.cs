using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Tokendial.Core.I18n;
using Tokendial.Linux.Install;

namespace Tokendial.Linux.Windows;

/// <summary>
/// The first thing a downloaded AppImage shows: what installing will do, in the words of the things it
/// writes, and two buttons.
/// </summary>
/// <remarks>
/// An AppImage that simply appears as a running application, with an Install button hidden in Settings, is
/// indistinguishable from something that let itself in. Windows has Setup.exe and macOS has a disk image you
/// drag into Applications; both are a visible, deliberate step before the app exists on the machine, and both
/// tell the user what is about to happen. This is that step. Installing ends with the installed copy running
/// and this one gone, so what the user is left with is the application, not the file they downloaded.
/// </remarks>
public sealed class SetupWindow : Window
{
    public SetupWindow()
    {
        Title = Strings.T("setup.title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Chrome.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Content = Build();
    }

    /// <summary>True to install, false to run this copy as it is.</summary>
    public event Action<bool>? Decided;

    private static string Version =>
        typeof(SetupWindow).Assembly.GetName().Version?.ToString(3) ?? "";

    private Control Build()
    {
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children =
            {
                Logo(),
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2,
                    Children = { Chrome.Heading(Strings.T("setup.title")), Chrome.Body($"Tokendial {Version}") }
                }
            }
        };

        var steps = new StackPanel { Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var key in new[] { "setup.step.copy", "setup.step.menu", "setup.step.launch" })
            steps.Children.Add(Step(Strings.T(key)));

        var safe = Chrome.Body(Strings.T("setup.safe"));
        safe.Margin = new Thickness(0, 14, 0, 0);

        var install = Chrome.Button(Strings.T("setup.install"), () => Answer(true), primary: true);
        var portable = Chrome.Button(Strings.T("setup.justRun"), () => Answer(false));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { portable, install }
        };

        return new StackPanel
        {
            Margin = new Thickness(24, 22, 24, 22),
            Children = { head, Chrome.Rule(new Thickness(0, 18, 0, 0)), steps, safe, buttons }
        };
    }

    /// <summary>
    /// One line of what will be written, marked by the same accent the dials use. A Grid rather than a
    /// horizontal StackPanel: a stack measures its children with unbounded width in the stacking direction,
    /// so the text never wraps and the longest line is simply cut off at the window edge.
    /// </summary>
    private static Control Step(string text)
    {
        var dot = new Border
        {
            Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Chrome.Accent,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 10, 0)
        };
        var body = Chrome.Body(text);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(dot);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static Control Logo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("tokendial-256.png");
        return new Image
        {
            Source = stream is null ? null : new Bitmap(stream),
            Width = 56, Height = 56,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void Answer(bool install)
    {
        Decided?.Invoke(install);
        Close();
    }
}
