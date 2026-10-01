using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Tokendial.Core.I18n;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;
using Tokendial.Linux.Install;
using Tokendial.Linux.Windows;

namespace Tokendial.Linux.Tests;

public sealed class SettingsWindowTests
{
    /// <summary>
    /// Every label in the window, read from the logical tree: the test application has no theme, so a
    /// ScrollViewer has no template and its content never joins the visual tree here.
    /// </summary>
    private static string Texts(Window window) => string.Join("\n",
        window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""));

    private static void WithWindow(Action<SettingsWindow> check)
    {
        var directory = Directory.CreateTempSubdirectory("tokendial-settings-");
        using var assistant = new InstallAssistant([]);
        var store = new UsageStore([], new ReadingArchive(directory.FullName));
        var window = new SettingsWindow(new Settings(), store, [], () => { }, () => { }, () => { }, assistant);
        try { check(window); }
        finally
        {
            window.Close();
            directory.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void RebuildingInAnotherLanguageKeepsEverySection() => WithWindow(window =>
    {
        var before = Texts(window);

        // The provider list, the display chooser and the menu hint are kept across rebuilds; adding one to
        // the new tree while the old one still holds it is what would throw here.
        window.Relocalize();
        window.Relocalize();

        Assert.Equal(before, Texts(window));
        Assert.Contains(Strings.T("settings.addToMenu"), before);
        Assert.Contains(Strings.T("settings.launchAtLogin"), before);
    });

    [AvaloniaFact]
    public void NoUpdateSwitchWhileNothingOnLinuxChecksForUpdates() => WithWindow(window =>
        Assert.DoesNotContain(Strings.T("settings.checkForUpdates"), Texts(window)));
}
