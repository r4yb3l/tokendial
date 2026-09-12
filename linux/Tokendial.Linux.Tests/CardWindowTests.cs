using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Tokendial.Core.Settings;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tests;

public class CardWindowTests
{
    [AvaloniaFact]
    public void HideInvalidatesPendingShow()
    {
        var card = new CardWindow();
        try
        {
            card.ShowAt(new Border { Width = 280, Height = 160 }, new(200, 200), new(0, 0, 1000, 800), 1);
            card.HideCard();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, card.Opacity);
        }
        finally { card.Shutdown(); }
    }

    [AvaloniaFact]
    public void CardUsesItsOwnScaleAndReclampsWhenContentGrows()
    {
        var card = new CardWindow();
        try
        {
            var area = new PixelRect(-1000, -100, 1000, 800);
            card.ShowAt(new Border { Width = 280, Height = 160 }, new(-500, 600), area, 2, DockEdge.Bottom);
            card.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            card.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, card.Opacity);
            Assert.Equal(-640, card.Position.X);
            Assert.Equal(440, card.Position.Y);
            card.Replace(new Border { Width = 280, Height = 400 });
            card.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(200, card.Position.Y);
        }
        finally { card.Shutdown(); }
    }
}
