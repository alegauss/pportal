using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP226: the first tests in this port that look at what a screen DRAWS.
///
/// Everything before them asserts what a view model says or what a source file still contains. The
/// one assertion worth making about a picture without a person looking at it is that there IS one:
/// a tree that failed to build renders as a rectangle of transparent pixels, and a caller that
/// checked only that a file was written could not tell the two apart.
/// </summary>
public class ScreenCaptureTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>PNG's own signature, so what is written is checked rather than assumed.</summary>
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ControllerMappingView MappingScreen()
    {
        ControllerMappingDocument document =
            ControllerMappingDocument.Parse(ScreenCapture.SampleDualSense, ScreenCapture.SampleName)!;

        var model = new ControllerMappingViewModel();
        model.Show(document);

        return new ControllerMappingView { DataContext = model };
    }

    /// <summary>A render is a PNG, and it is one because its first eight bytes say so.</summary>
    [Fact]
    public void ARenderIsAPng() => OnSta(() =>
    {
        byte[] png = ScreenCapture.Png(MappingScreen(), 900, 1500);

        Assert.True(png.Length > PngMagic.Length);
        Assert.Equal(PngMagic, png[..PngMagic.Length]);
    });

    /// <summary>
    /// And it drew something. This is the assertion that separates a picture from a blank, and it
    /// is the only claim made about the pixels: a screen that renders correctly and a screen that
    /// renders nothing both produce a file.
    /// </summary>
    [Fact]
    public void TheMappingScreenDrawsSomething()
        => OnSta(() => Assert.True(ScreenCapture.DrewSomething(MappingScreen(), 900, 1500)));

    /// <summary>
    /// The control that proves the check above can fail: an element with nothing in it renders as
    /// transparent, and a test that never saw a blank cannot claim to tell one.
    /// </summary>
    [Fact]
    public void AnEmptyElementDrawsNothing()
        => OnSta(() => Assert.False(ScreenCapture.DrewSomething(new Grid(), 200, 200)));

    /// <summary>And a background alone is enough to count, so the check is about pixels not children.</summary>
    [Fact]
    public void ABackgroundAloneIsSomething()
        => OnSta(() => Assert.True(
            ScreenCapture.DrewSomething(new Grid { Background = Brushes.Black }, 200, 200)));

    /// <summary>
    /// The fixture is a real pad's mapping string, and the screen built from it has the rows that
    /// string describes - which is what makes the picture one of something real.
    /// </summary>
    [Fact]
    public void TheFixtureIsARealPadsMapping()
    {
        ControllerMappingDocument? document =
            ControllerMappingDocument.Parse(ScreenCapture.SampleDualSense, ScreenCapture.SampleName);

        Assert.NotNull(document);

        // The `*` a DualSense really sends, resolved to what the pad calls itself.
        Assert.Equal(ScreenCapture.SampleName, document.ControllerType);

        Assert.Equal("b0", document.Physical("a").FirstOrDefault());
        Assert.Equal("h0.1", document.Physical("dpup").FirstOrDefault());
        Assert.Equal("a4", document.Physical("lefttrigger").FirstOrDefault());
    }

    /// <summary>
    /// The background is not a decoration. A screen drawn for a dark window renders as pale text on
    /// nothing when captured alone, which is the first picture this task produced: correct in every
    /// respect and unreadable.
    ///
    /// SystemColors was the obvious source and is measurably wrong - the classic Win32 palette
    /// answers white on a machine whose application window is dark - so what is asserted here is
    /// that the fallback is a real opaque brush rather than that particular trap.
    /// </summary>
    [Fact]
    public void ThereIsAlwaysABackgroundAndItIsNotTheSystemOne()
    {
        // No Application in a test run, so the theme key cannot answer and the fallback does.
        Assert.Null(Application.Current);
        Assert.Equal("the observed window colour", ScreenCapture.BackgroundSource);

        var brush = Assert.IsType<SolidColorBrush>(ScreenCapture.WindowBackground);

        Assert.Equal(byte.MaxValue, brush.Color.A);
        Assert.NotEqual(Colors.White, brush.Color);

        // FROZEN, or it belongs to whichever thread reached this class first and every capture
        // thread after that is refused. Found by this file's first run, not by reading.
        Assert.True(brush.IsFrozen);
    }

    /// <summary>
    /// And two captures on two different threads both work, which is the property the freeze buys.
    /// </summary>
    [Fact]
    public void TwoThreadsCanBothCapture()
    {
        OnSta(() => Assert.True(ScreenCapture.Png(new Grid(), 20, 20).Length > 0));
        OnSta(() => Assert.True(ScreenCapture.Png(new Grid(), 20, 20).Length > 0));
    }

    /// <summary>A caller may say what the window would have painted, and it is what gets painted.</summary>
    [Fact]
    public void ACallerCanNameTheBackground() => OnSta(() =>
    {
        byte[] onWhite = ScreenCapture.Png(new Grid(), 40, 40, Brushes.White);
        byte[] onBlack = ScreenCapture.Png(new Grid(), 40, 40, Brushes.Black);

        // An empty grid draws nothing at all, so any difference between these two is the
        // background and only the background.
        Assert.NotEqual(onWhite, onBlack);
    });

    /// <summary>A size of nothing is refused rather than rendered as an empty file.</summary>
    [Fact]
    public void ASizeOfNothingIsRefused() => OnSta(() =>
    {
        var view = MappingScreen();

        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.Png(view, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.Png(view, 100, -1));
    });
}
