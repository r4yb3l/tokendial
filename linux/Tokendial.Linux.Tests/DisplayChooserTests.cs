using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Linux.Windows;

namespace Tokendial.Linux.Tests;

public class DisplayChooserTests
{
    private static readonly ScreenInfo Primary = new("eDP-1", true,
        new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1);
    private static readonly ScreenInfo External = new("DP-1", false,
        new(-2560, 0, 2560, 1440), new(-2560, 0, 2560, 1440), 2);

    [AvaloniaFact]
    public void UnpluggedPreferenceRemainsVisibleAndCanBeCleared()
    {
        string? saved = External.Name;
        var chooser = new DisplayChooser();
        void Choose(string? value) => saved = value;
        chooser.Update([Primary, External], saved, Choose);
        Assert.Equal(2, chooser.Children.Count);

        chooser.Update([Primary], saved, Choose);
        Assert.Equal(External.Name, saved);
        var absent = Assert.IsType<TextBlock>(chooser.Children[2]);
        Assert.Contains(External.Name, absent.Text);
        var chips = Assert.IsType<WrapPanel>(chooser.Children[1]);
        Assert.Equal(2, chips.Children.Count);
        Assert.IsType<RadioButton>(chips.Children[0]).IsChecked = true;
        Assert.Null(saved);
    }

    [AvaloniaFact]
    public void ReconnectRemovesWarningAndRestoresSelectedChip()
    {
        var chooser = new DisplayChooser();
        chooser.Update([Primary], External.Name, _ => { });
        Assert.Equal(3, chooser.Children.Count);
        chooser.Update([External, Primary], External.Name, _ => { });
        Assert.Equal(2, chooser.Children.Count);
        var chips = Assert.IsType<WrapPanel>(chooser.Children[1]);
        var external = Assert.IsType<RadioButton>(chips.Children[1]);
        Assert.True(external.IsChecked);
        external.ApplyTemplate();
        Assert.Contains(external.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == External.Name);
    }

    [AvaloniaFact]
    public void AutomaticSingleMonitorNeedsNoChooserButSavedPreferenceDoes()
    {
        var chooser = new DisplayChooser();
        chooser.Update([Primary], null, _ => { });
        Assert.Empty(chooser.Children);
        chooser.Update([Primary], Primary.Name, _ => { });
        Assert.Equal(2, chooser.Children.Count);
        var chips = Assert.IsType<WrapPanel>(chooser.Children[1]);
        var automatic = Assert.IsType<RadioButton>(chips.Children[0]);
        automatic.ApplyTemplate();
        Assert.Contains(automatic.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == Strings.T("settings.screen.automatic"));
    }

    [AvaloniaFact]
    public void NoScreensStillExplainsMissingPreference()
    {
        var chooser = new DisplayChooser();
        chooser.Update([], External.Name, _ => { });
        Assert.Contains(External.Name, Assert.IsType<TextBlock>(chooser.Children[2]).Text);
    }
}
