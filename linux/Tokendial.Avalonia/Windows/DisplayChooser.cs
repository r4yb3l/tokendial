using Avalonia;
using Avalonia.Controls;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;

namespace Tokendial.Linux.Windows;

/// <summary>Keeps the saved monitor visible as a preference even while that monitor is unplugged.</summary>
public sealed class DisplayChooser : StackPanel
{
    public void Update(IReadOnlyList<ScreenInfo> attached, string? saved, Action<string?> choose)
    {
        Children.Clear();
        if (attached.Count < 2 && string.IsNullOrEmpty(saved)) return;

        var title = Chrome.SmallTitle(Strings.T("settings.screen"));
        title.Margin = new Thickness(0, 18, 0, 8);
        Children.Add(title);

        var chips = new WrapPanel();
        void Chip(string label, string? value)
        {
            var chip = Chrome.Chip("panelScreen", label, saved == value, () => choose(value));
            chip.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(chip);
        }

        Chip(Strings.T("settings.screen.automatic"), null);
        foreach (var screen in attached) Chip(screen.Name, screen.Name);
        Children.Add(chips);

        if (Displays.Missing(attached, saved))
        {
            var absent = Chrome.Body(Strings.T("settings.screen.absent", ("name", saved ?? "")));
            absent.FontSize = 11;
            absent.Foreground = Panel.Theme.TextDisabled;
            Children.Add(absent);
        }
    }
}
