using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.I18n;

namespace Tokendial.Linux.Windows;

/// <summary>Cancel is first so that a dialog dismissed by the window manager answers it rather than Yes.</summary>
public enum Answer { Cancel, Yes, No }

/// <summary>
/// The question a message box asks, in Tokendial's own vocabulary.
/// </summary>
/// <remarks>
/// WPF has <c>MessageBox</c> and windows/Tokendial.App/Windows/Uninstall.cs uses it; Avalonia has nothing
/// equivalent, and the alternative - a GTK dialog over the desktop portal - would arrive dressed as another
/// application in the middle of this one. Three buttons and a paragraph is little enough to own.
/// </remarks>
public static class Ask
{
    public static Task<Answer> YesNoCancel(Window owner, string title, string message) =>
        Show(owner, title, message,
        [
            (Strings.T("dialog.yes"), Answer.Yes, false),
            (Strings.T("dialog.no"), Answer.No, false),
            (Strings.T("dialog.cancel"), Answer.Cancel, true)
        ]);

    public static Task<Answer> Confirm(Window owner, string title, string message, string confirm) =>
        Show(owner, title, message,
        [
            (confirm, Answer.Yes, false),
            (Strings.T("dialog.cancel"), Answer.Cancel, true)
        ]);

    public static Task Tell(Window owner, string title, string message) =>
        Show(owner, title, message, [(Strings.T("dialog.ok"), Answer.Yes, true)]);

    private static Task<Answer> Show(Window owner, string title, string message,
        IReadOnlyList<(string Label, Answer Answer, bool Primary)> choices)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = Chrome.WindowBackground,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        foreach (var (label, answer, primary) in choices)
            buttons.Children.Add(Chrome.Button(label, () => window.Close(answer), primary));

        var body = Chrome.Body(message);
        body.LineHeight = 19;

        window.Content = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 20),
            Children = { Chrome.Heading(title), Spacer(), body, buttons }
        };

        return window.ShowDialog<Answer>(owner);
    }

    private static Control Spacer() => new Border { Height = 10 };
}
