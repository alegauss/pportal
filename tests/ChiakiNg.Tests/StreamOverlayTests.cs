using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP10: the overlay's rules, which are units and bit tests rather than layout.
/// </summary>
public class StreamOverlayTests
{
    /// <summary>
    /// The black panel is up for three different reasons, and the third is not a transient: video
    /// switched off in settings keeps it up for the whole session.
    /// </summary>
    [Fact]
    public void ThePanelIsUpForLoadingForFailureAndForDisabledVideo()
    {
        var model = new StreamOverlayViewModel { Loading = false };
        Assert.False(model.LoadingVisible);

        model.Loading = true;
        Assert.True(model.LoadingVisible);

        model.Loading = false;
        model.Error = true;
        Assert.True(model.LoadingVisible);

        model.Error = false;
        model.Disabled = AudioVideoDisabled.Video;
        Assert.True(model.LoadingVisible);
    }

    /// <summary>
    /// And audio alone does NOT put it up. The mask is two independent bits, and a port that read
    /// it as three choices would black the screen out for a user who only muted the sound.
    /// </summary>
    [Fact]
    public void DisablingAudioAloneShowsNoPanel()
    {
        var model = new StreamOverlayViewModel { Loading = false, Disabled = AudioVideoDisabled.Audio };

        Assert.False(model.LoadingVisible);
        Assert.False(model.DisabledNoticeVisible);
    }

    /// <summary>
    /// The notice is shown only once the session is neither loading nor failed, though the panel
    /// behind it is up for all three - one panel, three reasons, and they do not stack.
    /// </summary>
    [Fact]
    public void TheDisabledNoticeWaitsForLoadingAndFailureToClear()
    {
        var model = new StreamOverlayViewModel { Disabled = AudioVideoDisabled.Video };

        Assert.True(model.LoadingVisible);
        Assert.False(model.DisabledNoticeVisible);

        model.Loading = false;
        Assert.True(model.DisabledNoticeVisible);

        model.Error = true;
        Assert.False(model.DisabledNoticeVisible);
    }

    /// <summary>The heading reads the mask a second time, to say whether the sound went too.</summary>
    [Theory]
    [InlineData(AudioVideoDisabled.Video, "Video Disabled")]
    [InlineData(AudioVideoDisabled.Video | AudioVideoDisabled.Audio, "Audio and Video Disabled")]
    public void TheHeadingNamesWhicheverBitsAreSet(AudioVideoDisabled disabled, string expected)
        => Assert.Equal(expected, new StreamOverlayViewModel { Disabled = disabled }.DisabledTitle);

    /// <summary>
    /// The error labels are shown by their own text and by nothing else. Nothing in the QML ever
    /// sets their visibility, so an empty error is an empty screen rather than a blank label.
    /// </summary>
    [Fact]
    public void TheErrorLabelsAreShownByTheirTextAlone()
    {
        var model = new StreamOverlayViewModel { Error = true };

        Assert.False(model.ErrorTitleVisible);
        Assert.False(model.ErrorTextVisible);

        model.ErrorTitle = "Session Error";
        Assert.True(model.ErrorTitleVisible);
        Assert.False(model.ErrorTextVisible);
    }

    /// <summary>The can't-display notice needs a picture to be covering.</summary>
    [Fact]
    public void TheCantDisplayNoticeNeedsAFrameFirst()
    {
        var model = new StreamOverlayViewModel { CantDisplay = true };
        Assert.False(model.CantDisplayVisible);

        model.HasVideo = true;
        Assert.True(model.CantDisplayVisible);
    }

    /// <summary>
    /// The indicator's threshold, and the unit mismatch it is built on: the measure is a fraction
    /// and the setting is a whole percent. 3% lights at 0.031 and not at 0.03.
    /// </summary>
    [Fact]
    public void TheIndicatorComparesAFractionAgainstAPercent()
    {
        var model = new StreamOverlayViewModel { DroppedNotifyPercent = 3, PacketLoss = 0.02 };
        Assert.False(model.NetworkIndicatorVisible);

        model.PacketLoss = 0.031;
        Assert.True(model.NetworkIndicatorVisible);

        // The trap in the other direction: a port comparing 3.1 against 3 would light here too,
        // and a port comparing 0.031 against 3 would never light at all.
        model.PacketLoss = 2.0;
        model.DroppedNotifyPercent = 300;
        Assert.False(model.NetworkIndicatorVisible);
    }

    /// <summary>A loss that is not a number yet does not light it either.</summary>
    [Fact]
    public void ANonFiniteLossLightsNothing()
        => Assert.False(new StreamOverlayViewModel { PacketLoss = double.NaN }.NetworkIndicatorVisible);

    /// <summary>
    /// The four conversions in the readouts. The frame age is the one that matters most: read in
    /// seconds and printed in milliseconds, so a port that bound it straight would print 0 ms
    /// through the whole of a bad session.
    /// </summary>
    [Fact]
    public void TheReadoutsCarryTheirUnits()
    {
        Assert.Equal("12.3", StreamStats.Bitrate(12.34));
        Assert.Equal("1.5", StreamStats.QueueDepth(1.46));
        Assert.Equal("42 ms", StreamStats.PendingFrameAge(0.0421));
        Assert.Equal("3.1%", StreamStats.PacketLoss(0.0314));
        Assert.Equal("17", StreamStats.DroppedFrames(17));
    }

    /// <summary>
    /// A midpoint that is not one. 1.45 is 1.4499999999999999556 as a double, and BOTH toFixed and
    /// "F1" round the binary value rather than the decimal it was typed as - so both print 1.4.
    ///
    /// Asserted because the obvious expectation is 1.5, and a port that "fixed" this by rounding
    /// the decimal would disagree with the Qt client on every reading that lands on a half.
    /// </summary>
    [Fact]
    public void AHalfThatIsNotAHalfRoundsDownInBothClients()
        => Assert.Equal("1.4", StreamStats.QueueDepth(1.45));

    /// <summary>
    /// The two guarded readouts, which the QML wraps in isFinite because both are divisions with
    /// no samples to divide at the start of a session. "NaN%" is what a port shows instead.
    /// </summary>
    [Fact]
    public void TheAveragedReadoutsShowZeroBeforeTheyHaveSamples()
    {
        Assert.Equal("0.0%", StreamStats.PacketLoss(double.NaN));
        Assert.Equal("0.0%", StreamStats.PacketLoss(double.PositiveInfinity));
        Assert.Equal("0", StreamStats.LostFrames(double.NaN));
    }

    /// <summary>
    /// The hint's shortcut, which is the controller's when there is one and the literal Ctrl+O
    /// when there is not - spelled in the QML rather than looked up, so a port that asked the
    /// settings for it would print whatever the keyboard map happened to hold.
    /// </summary>
    [Fact]
    public void TheHintNamesTheKeyboardShortcutWithNoController()
    {
        Assert.Contains("Ctrl+O", StreamOverlayViewModel.LoadingHintFor(
            "L3+R3", hasController: false, AudioVideoDisabled.None, null), StringComparison.Ordinal);

        Assert.Contains("L3+R3", StreamOverlayViewModel.LoadingHintFor(
            "L3+R3", hasController: true, AudioVideoDisabled.None, null), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the audio line appears for audio ALONE. The QML tests the whole mask for equality, so a
    /// port testing the bit would show "Audio Disabled in settings" on a screen that is black
    /// because video is off too.
    /// </summary>
    [Fact]
    public void TheAudioLineIsForAudioAlone()
    {
        Assert.StartsWith("Audio Disabled in settings", StreamOverlayViewModel.LoadingHintFor(
            "L3+R3", true, AudioVideoDisabled.Audio, null), StringComparison.Ordinal);

        Assert.DoesNotContain("Audio Disabled", StreamOverlayViewModel.LoadingHintFor(
            "L3+R3", true, AudioVideoDisabled.Audio | AudioVideoDisabled.Video, null),
            StringComparison.Ordinal);
    }

    /// <summary>The dpad line is there only when the setting gives a shortcut to name.</summary>
    [Fact]
    public void TheDpadLineFollowsTheSetting()
    {
        Assert.DoesNotContain("dpad touch", StreamOverlayViewModel.LoadingHintFor(
            "L3+R3", true, AudioVideoDisabled.None, null), StringComparison.Ordinal);

        Assert.Contains("dpad touch", StreamOverlayViewModel.LoadingHintFor(
            "L3+R3", true, AudioVideoDisabled.None, "Ctrl+T"), StringComparison.Ordinal);
    }

    /// <summary>Every rule above, still stated the same way in the QML it was read from.</summary>
    [Fact]
    public void TheOverlaysRulesAreStillTheQtClients()
    {
        string? file = StreamOverlaySource.Locate();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.True(StreamOverlaySource.TheLoadingPanelHasThreeReasons(qml), "three reasons");
        Assert.True(StreamOverlaySource.VisibilityIsStillBoundToOpacity(qml), "visible: opacity");
        Assert.True(StreamOverlaySource.TheErrorLabelsAreShownByTheirText(qml), "visible: text");
        Assert.True(StreamOverlaySource.TheAudioHintTestsTheWholeMask(qml), "== 0x01");
        Assert.True(StreamOverlaySource.TheKeyboardShortcutIsSpeltOut(qml), "the literal Ctrl+O");
        Assert.True(StreamOverlaySource.TheIndicatorScalesTheSetting(qml), "the setting is scaled");
        Assert.True(StreamOverlaySource.TheFrameAgeIsConvertedAtTheLabel(qml), "seconds to ms");
        Assert.True(StreamOverlaySource.TheAveragesAreGuarded(qml), "isFinite on both averages");
    }
}
