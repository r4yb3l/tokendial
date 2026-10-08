using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Tokendial.Core.Model;
using Tokendial.Linux.Tray;

namespace Tokendial.Linux.Tests;

/// <summary>
/// The tray icon is rebuilt only when what it draws changes. Every rebuild used to go through Skia's PNG
/// encoder, which segfaults intermittently in libSkiaSharp 3.119.4 on AVX-512 machines, so a refresh that
/// changes nothing on screen must not touch it.
/// </summary>
public sealed class TrayIconTests
{
    [Fact]
    public void ReadingsWithinOnePercentDrawTheSameGlyph()
    {
        Assert.Equal(TrayGlyph.Of(0.421), TrayGlyph.Of(0.424));
        Assert.Equal(new TrayGlyph(Band.Ample, 42), TrayGlyph.Of(0.421));
    }

    [Fact]
    public void ABandBoundaryChangesTheGlyphEvenWhenThePercentRoundsTheSame()
    {
        Assert.Equal(new TrayGlyph(Band.Ample, 50), TrayGlyph.Of(0.498));
        Assert.Equal(new TrayGlyph(Band.Watch, 50), TrayGlyph.Of(0.502));
    }

    [Fact]
    public void NoReadingAndANegligibleOneBothDrawTheEmptyDial()
    {
        Assert.Equal(TrayGlyph.Empty, TrayGlyph.Of(null));
        Assert.Equal(TrayGlyph.Empty, TrayGlyph.Of(0.004));
        Assert.Equal(TrayGlyph.Empty, TrayGlyph.Of(0.01));
        Assert.NotEqual(TrayGlyph.Empty, TrayGlyph.Of(0.011));
    }

    [Fact]
    public void AnOverrunDrawsAFullCriticalDial()
    {
        Assert.Equal(new TrayGlyph(Band.Critical, 100), TrayGlyph.Of(1.37));
    }

    [AvaloniaFact]
    public void AnUpdateThatDrawsTheSameGlyphDoesNotRebuildTheIcon()
    {
        var rendered = new List<TrayGlyph>();
        using var tray = new Tokendial.Linux.Tray.TrayIcon(new Application(), glyph =>
        {
            rendered.Add(glyph);
            return new WindowIcon(new MemoryStream(TrayPng.Encode(1, 1, [0, 0, 0, 0])));
        });

        tray.Update(0.421, "Claude 42%");
        tray.Update(0.424, "Claude 42%");
        tray.Update(0.81, "Claude 81%");
        tray.Update(0.812, "Claude 81%");
        tray.Update(null, "Tokendial");
        tray.Update(0.004, "Tokendial");

        Assert.Equal(
            [TrayGlyph.Empty, new TrayGlyph(Band.Ample, 42), new TrayGlyph(Band.Critical, 81), TrayGlyph.Empty],
            rendered);
    }

    [Fact]
    public void ThePngDecodesToTheUnpremultipliedPixels()
    {
        byte[] premultipliedBgra =
        [
            0, 0, 255, 255,     0, 0, 0, 0,
            10, 0, 51, 51,      255, 255, 255, 255,
        ];

        var png = TrayPng.Encode(2, 2, premultipliedBgra);

        using var decoded = SKBitmap.Decode(png, new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.NotNull(decoded);
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,     0, 0, 0, 0,
                255, 0, 50, 51,     255, 255, 255, 255,
            },
            decoded.Bytes);
    }
}
