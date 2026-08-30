using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP10: the in-stream menu's rules, which are writes rather than readouts.
/// </summary>
public class StreamMenuTests
{
    /// <summary>
    /// The two enums, transcribed from the header rather than from the row of buttons.
    ///
    /// Stretch is 1 and Zoom is 2 though the menu draws Zoom first, and Default is 1 because a
    /// Fast preset the menu never offers holds 0. Both numbers reach a settings file, so both are
    /// asserted as values and not as names.
    /// </summary>
    [Fact]
    public void TheEnumValuesAreTheHeadersAndNotTheMenusOrder()
    {
        Assert.Equal(0, (int)StreamVideoMode.Normal);
        Assert.Equal(1, (int)StreamVideoMode.Stretch);
        Assert.Equal(2, (int)StreamVideoMode.Zoom);

        Assert.Equal(0, (int)StreamVideoPreset.Fast);
        Assert.Equal(1, (int)StreamVideoPreset.Default);
        Assert.Equal(5, (int)StreamVideoPreset.Custom);
    }

    /// <summary>
    /// The mic button is lit when the microphone is LIVE. Inverted from the property it reads, and
    /// a port that bound it straight would show every muted session as unmuted.
    /// </summary>
    [Fact]
    public void TheMicButtonIsLitWhenTheMicIsNotMuted()
    {
        var model = new StreamMenuViewModel { SessionActive = true, Connected = true };

        Assert.True(model.MicOn);
        Assert.True(model.MicEnabled);

        model.Muted = true;
        Assert.False(model.MicOn);

        // Still enabled: a muted mic can be unmuted, which is the whole point of the button.
        Assert.True(model.MicEnabled);
    }

    /// <summary>
    /// And the press flips MUTED rather than the lit state. Written this way because the other way
    /// round is the same inversion one step later: a button that set itself and then told the
    /// session would look right and mute backwards.
    /// </summary>
    [Fact]
    public void PressingTheMicButtonFlipsMuted()
    {
        var model = new StreamMenuViewModel { SessionActive = true, Connected = true };

        model.ToggleMic();
        Assert.True(model.Muted);
        Assert.False(model.MicOn);

        model.ToggleMic();
        Assert.False(model.Muted);
        Assert.True(model.MicOn);
    }

    /// <summary>And it is pressable only while the session is connected, not merely alive.</summary>
    [Fact]
    public void TheMicButtonNeedsAConnectedSession()
    {
        var model = new StreamMenuViewModel { SessionActive = true, Connected = false };

        Assert.False(model.MicEnabled);
        Assert.True(model.MicOn);
    }

    /// <summary>
    /// Zoom and Stretch are one three-valued property behind two checkboxes: pressing the lit one
    /// selects Normal, and pressing the other takes over rather than adding to it.
    /// </summary>
    [Fact]
    public void TheTwoModeButtonsShareOneProperty()
    {
        var model = new StreamMenuViewModel();

        model.Toggle(StreamVideoMode.Zoom);
        Assert.Equal(StreamVideoMode.Zoom, model.VideoMode);

        model.Toggle(StreamVideoMode.Zoom);
        Assert.Equal(StreamVideoMode.Normal, model.VideoMode);

        model.Toggle(StreamVideoMode.Zoom);
        model.Toggle(StreamVideoMode.Stretch);
        Assert.Equal(StreamVideoMode.Stretch, model.VideoMode);
    }

    /// <summary>The zoom slider is on show for the zoom mode and no other.</summary>
    [Fact]
    public void TheZoomSliderFollowsTheMode()
    {
        var model = new StreamMenuViewModel();
        Assert.False(model.ZoomFactorVisible);

        model.VideoMode = StreamVideoMode.Zoom;
        Assert.True(model.ZoomFactorVisible);

        model.VideoMode = StreamVideoMode.Stretch;
        Assert.False(model.ZoomFactorVisible);
    }

    /// <summary>
    /// The zoom label's three branches over two numbers. The floor is a word, zero and up carries
    /// a +1 offset, and the range between them is printed unchanged - which is the branch a port
    /// applying the offset everywhere gets wrong, and it gets it wrong exactly where the picture
    /// is being cropped.
    /// </summary>
    [Theory]
    [InlineData(-1.0, "No Black Bars")]
    [InlineData(0.0, "1.00 x")]
    [InlineData(4.0, "5.00 x")]
    [InlineData(-0.5, "-0.50 x")]
    [InlineData(-0.99, "-0.99 x")]
    public void TheZoomLabelOffsetsOnlyFromZeroUp(double value, string expected)
        => Assert.Equal(expected, StreamMenuViewModel.ZoomCaptionFor(value));

    /// <summary>The volume is a percentage on screen and 0..128 in the store.</summary>
    [Theory]
    [InlineData(128, "100% Volume")]
    [InlineData(64, "50% Volume")]
    [InlineData(0, "0% Volume")]
    public void TheVolumeLabelIsAPercentOfOneHundredAndTwentyEight(int volume, string expected)
        => Assert.Equal(expected, new StreamMenuViewModel { Volume = volume }.VolumeCaption);

    /// <summary>
    /// The close button means two things. With a session it closes the window; without one it
    /// asks for the main view, and nothing on the button says which it will do.
    /// </summary>
    [Fact]
    public void TheCloseButtonMeansTwoThings()
    {
        Assert.True(new StreamMenuViewModel().CloseGoesToTheMainView);
        Assert.False(new StreamMenuViewModel { SessionActive = true }.CloseGoesToTheMainView);
    }

    /// <summary>The Placebo button belongs to the Custom preset and to nothing else.</summary>
    [Fact]
    public void ThePlaceboButtonBelongsToTheCustomPreset()
    {
        var model = new StreamMenuViewModel { VideoPreset = StreamVideoPreset.HighQuality };
        Assert.False(model.PlaceboVisible);

        model.VideoPreset = StreamVideoPreset.Custom;
        Assert.True(model.PlaceboVisible);
    }

    /// <summary>
    /// The menu hides its dropped-frames line when the count is zero, where the HUD shows the same
    /// value always. Two screens, one number, two rules - reproduced rather than reconciled.
    /// </summary>
    [Fact]
    public void TheMenusDroppedLineNeedsANonZeroCount()
    {
        var model = new StreamMenuViewModel { SessionActive = true };
        Assert.False(model.DroppedFramesVisible);

        model.DroppedFrames = 1;
        Assert.True(model.DroppedFramesVisible);

        var hud = new StreamOverlayViewModel { SessionActive = true, DroppedFrameCount = 0 };
        Assert.True(hud.StatsVisible);
        Assert.Equal("0", hud.DroppedFrames);
    }

    /// <summary>The console line, and streamer mode replacing the name rather than the sentence.</summary>
    [Fact]
    public void TheConsoleLineHidesTheNameAndKeepsTheSentence()
    {
        var model = new StreamMenuViewModel { SessionActive = true, Host = "PS5-1234" };
        Assert.Equal("Connecting to PS5-1234", model.ConsoleCaption);

        model.Connected = true;
        Assert.Equal("Connected to PS5-1234", model.ConsoleCaption);

        model.StreamerMode = true;
        Assert.Equal("Connected to hidden", model.ConsoleCaption);
    }

    /// <summary>With no session the line is empty rather than a sentence about nothing.</summary>
    [Fact]
    public void WithNoSessionTheConsoleLineIsEmpty()
        => Assert.Equal("", new StreamMenuViewModel { Host = "PS5-1234" }.ConsoleCaption);

    /// <summary>
    /// PP575, PP10: the menu stores the five presets it can draw, and not the sixth.
    ///
    /// Fast is the whole content of this rule. It holds 0, the row of buttons starts at Default,
    /// and no press on this screen can write it - so a check answering "is this a member of the
    /// enum" says yes for the one preset the answer is no for. That was the body until this test
    /// existed to disagree with it.
    /// </summary>
    [Fact]
    public void OnlyThePresetsWithButtonsAreStored()
    {
        Assert.False(StreamMenuViewModel.PresetIsPersisted(StreamVideoPreset.Fast));

        Assert.True(StreamMenuViewModel.PresetIsPersisted(StreamVideoPreset.Default));
        Assert.True(StreamMenuViewModel.PresetIsPersisted(StreamVideoPreset.HighQuality));
        Assert.True(StreamMenuViewModel.PresetIsPersisted(StreamVideoPreset.HighQualitySpatial));
        Assert.True(StreamMenuViewModel.PresetIsPersisted(StreamVideoPreset.HighQualityAdvancedSpatial));
        Assert.True(StreamMenuViewModel.PresetIsPersisted(StreamVideoPreset.Custom));

        // And a value that is in neither set. `Enum.IsDefined` is still the first half of the
        // rule, so a cast from an int off the end of the enum is not persisted either.
        Assert.False(StreamMenuViewModel.PresetIsPersisted((StreamVideoPreset)9));
    }

    /// <summary>Every rule above, still stated the same way in the QML and the header.</summary>
    [Fact]
    public void TheMenusRulesAreStillTheQtClients()
    {
        string? menu = StreamMenuSource.Locate(StreamMenuSource.MenuQml);
        string? header = StreamMenuSource.Locate(StreamMenuSource.WindowHeader);
        if (menu is null || header is null)
            return;

        string qml = File.ReadAllText(menu);

        Assert.True(StreamMenuSource.TheMicButtonIsStillInverted(qml), "the mic button is inverted");
        Assert.True(StreamMenuSource.TheVideoModeIsNotPersisted(qml), "the mode is not stored");
        Assert.True(StreamMenuSource.TheZoomFactorIsPersisted(qml), "the factor beside it is");
        Assert.True(StreamMenuSource.TheZoomLabelOffsetsOnlyTheUpperBranch(qml), "three branches");
        Assert.True(StreamMenuSource.ThePlaceboButtonNeedsTheCustomPreset(qml), "Custom only");
        Assert.True(StreamMenuSource.TheCloseButtonHasTwoMeanings(qml), "two closes");
        Assert.True(StreamMenuSource.TheDroppedLineNeedsANonZeroCount(qml), "non-zero only");
        Assert.True(StreamMenuSource.TheEnumsAreStillInThisOrder(File.ReadAllText(header)),
            "Stretch before Zoom, and Fast ahead of Default");

        // PP575: the per-preset rule, held against the buttons rather than restated. Every preset
        // the model calls persisted writes both `Chiaki.window.videoPreset` and
        // `Chiaki.settings.videoPreset`; Fast writes neither, because it has no button to press.
        Assert.True(StreamMenuSource.EveryPresetAgreesWithTheMenu(qml),
            "five buttons write the window and the setting, and Fast has neither");
    }
}
