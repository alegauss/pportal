using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP719: chiaki_session_send_event, and the nine events the frame path raises through it.
///
/// PP712's census owed PP707's host a SendConnected, and the row's reason was that nothing managed
/// raises a session event at all - StreamRun reads CHIAKI_EVENT_CONNECTED off the C session and no
/// code here sends one. So these hold PP707's second criterion as well: shipping a subsystem is
/// supposed to shorten that list, and StreamRunHostConsumersTests asserts that it did.
///
/// THREE THINGS ARE WORTH ASSERTING and the rest is a switch. The send returns where nobody is
/// listening, which is what lets every raiser be unconditional. Each raiser fills its own arm of a
/// union and leaves the rest zero. And the two parses keep the C's size guards, under which it logs
/// and raises nothing rather than raising a short event.
/// </summary>
public class ManagedSessionEventsTests(ITestOutputHelper output)
{
    /// <summary>Every event that reached a sink, in order.</summary>
    private sealed class Listener : ISessionEventSink
    {
        private readonly List<SessionEvent> heard = [];

        public IReadOnlyList<SessionEvent> Heard => heard;

        public void Send(in SessionEvent raised) => heard.Add(raised);
    }

    private static string? Read(string relativePath)
    {
        string? path = ManagedSessionEventsSource.Locate(relativePath);

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE NULL CALLBACK: raising with nobody listening does nothing and says so.
    ///
    /// The C's whole body is this test and the call. A port that threw, or that took a sink in its
    /// constructor, would turn a session with no application attached into a failure on the first
    /// event - and every raiser in streamconnection.c is written as though it cannot fail.
    /// </summary>
    [Fact]
    public void RaisingWithNoCallbackDropsTheEventAndReportsIt()
    {
        var events = new ManagedSessionEvents();

        Assert.False(events.IsHeard);
        Assert.False(events.SendConnected());

        output.WriteLine($"sent {events.Sent}, unheard {events.Unheard}");

        Assert.Equal(0, events.Sent);
        Assert.Equal(1, events.Unheard);
    }

    /// <summary>And with one attached it goes, which is the other half of the same guard.</summary>
    [Fact]
    public void ListeningTakesTheEventAndDetachingStopsIt()
    {
        var events = new ManagedSessionEvents();
        var listener = new Listener();

        events.Listen(listener);
        Assert.True(events.SendConnected());

        events.Listen(null);
        Assert.False(events.SendConnected());

        Assert.Equal([ManagedSessionEvents.Connected()], listener.Heard);
        Assert.Equal(1, events.Sent);
        Assert.Equal(1, events.Unheard);
    }

    /// <summary>
    /// THE ZERO IS THE BEHAVIOUR: an event carries its own arm and nothing else.
    ///
    /// <c>ChiakiEvent event = { 0 }</c> at every raiser, so a light-bar event reports player index
    /// zero rather than whichever player the pad last was. A port holding one payload object per
    /// event kind would have no way to be wrong about this and no way to be right either.
    /// </summary>
    [Fact]
    public void AnEventFillsOnlyItsOwnArmOfTheUnion()
    {
        SessionEvent led = ManagedSessionEvents.ForPad(
            PadReportKind.LedColor, new PadState(7, new PadLed(9, 8, 7), 1, 3));

        output.WriteLine($"{led.Type}: led {led.Led}, player {led.PlayerIndex}, intensity {led.Intensity}");

        Assert.Equal(ChiakiEventType.LedColor, led.Type);
        Assert.Equal(new PadLed(9, 8, 7), led.Led);

        // The three the state HAD and this event does not carry.
        Assert.Equal(0, led.PlayerIndex);
        Assert.Equal(DualSenseEffectIntensity.Off, led.Intensity);
        Assert.Equal(default, led.Rumble);
    }

    /// <summary>The motion reset is the extreme case: a type and an entirely zero union.</summary>
    [Fact]
    public void TheMotionResetCarriesNothingAtAll()
        => Assert.Equal(
            new SessionEvent(ChiakiEventType.MotionReset),
            ManagedSessionEvents.ForPad(
                PadReportKind.MotionReset, new PadState(4, new PadLed(1, 1, 1), 2, 2)));

    /// <summary>Under three bytes the C logs the size and raises nothing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ARumbleUnderThreeBytesRaisesNothing(int length)
        => Assert.Null(ManagedSessionEvents.Rumble(new byte[length]));

    /// <summary>And at three or above it is the first three bytes, whatever else arrived.</summary>
    [Fact]
    public void ARumbleIsTheFirstThreeBytesAndTheRestIsIgnored()
    {
        SessionEvent? three = ManagedSessionEvents.Rumble([0x11, 0x22, 0x33]);
        SessionEvent? longer = ManagedSessionEvents.Rumble([0x11, 0x22, 0x33, 0xFF, 0xFF]);

        Assert.Equal(
            new SessionEvent(ChiakiEventType.Rumble, Rumble: new RumbleState(0x11, 0x22, 0x33)),
            three);

        // The same event: nothing past the third byte reaches it.
        Assert.Equal(three, longer);
    }

    /// <summary>Under 0x19 the trigger effects handler does the same.</summary>
    [Fact]
    public void TriggerEffectsUnderTwentyFiveBytesRaiseNothing()
    {
        Assert.Null(ManagedSessionEvents.TriggerEffects(
            new byte[ManagedSessionEvents.TriggerEffectsMinimum - 1]));

        Assert.NotNull(ManagedSessionEvents.TriggerEffects(
            new byte[ManagedSessionEvents.TriggerEffectsMinimum]));
    }

    /// <summary>
    /// The four fields come from the offsets the C reads, and three bytes are read by nobody.
    ///
    /// buf[0], buf[3] and buf[4] sit between the fields and the C never touches them, so rewriting
    /// them produces the same event. That is the assertion a port copying the layout by eye fails:
    /// the two ten-byte blobs start at 5 and 15, not at 3 and 13.
    /// </summary>
    [Fact]
    public void TriggerEffectsComeFromTheOffsetsTheCReads()
    {
        byte[] payload = new byte[ManagedSessionEvents.TriggerEffectsMinimum];
        payload[ManagedSessionEvents.TriggerTypeLeftOffset] = 0x21;
        payload[ManagedSessionEvents.TriggerTypeRightOffset] = 0x26;

        for (var at = 0; at < TriggerEffectsState.SideBytes; at++)
        {
            payload[ManagedSessionEvents.TriggerLeftOffset + at] = (byte)(0xA0 + at);
            payload[ManagedSessionEvents.TriggerRightOffset + at] = (byte)(0xB0 + at);
        }

        SessionEvent raised = Assert.NotNull(ManagedSessionEvents.TriggerEffects(payload));

        output.WriteLine(
            $"left {raised.TriggerEffects.TypeLeft:x2} {Convert.ToHexString(raised.TriggerEffects.LeftBytes)}");

        Assert.Equal(ChiakiEventType.TriggerEffects, raised.Type);
        Assert.Equal(0x21, raised.TriggerEffects.TypeLeft);
        Assert.Equal(0x26, raised.TriggerEffects.TypeRight);
        Assert.Equal("A0A1A2A3A4A5A6A7A8A9", Convert.ToHexString(raised.TriggerEffects.LeftBytes));
        Assert.Equal("B0B1B2B3B4B5B6B7B8B9", Convert.ToHexString(raised.TriggerEffects.RightBytes));

        // The three the layout skips: the same event comes back with them set to anything.
        payload[0] = 0xFF;
        payload[3] = 0xFF;
        payload[4] = 0xFF;

        Assert.Equal(raised, ManagedSessionEvents.TriggerEffects(payload));
    }

    /// <summary>
    /// Two events carrying the same effect are equal, which a record over arrays would deny.
    ///
    /// <see cref="PadLed"/>'s reason one payload over. The blobs are compared by content, so a test
    /// asserting on an effect asserts about the bytes rather than about which allocation they are.
    /// </summary>
    [Fact]
    public void TwoTriggerEffectsWithTheSameBytesAreEqual()
    {
        byte[] payload = [.. Enumerable.Range(0, ManagedSessionEvents.TriggerEffectsMinimum).Select(one => (byte)one)];

        Assert.Equal(
            ManagedSessionEvents.TriggerEffects(payload),
            ManagedSessionEvents.TriggerEffects([.. payload]));

        Assert.Equal(
            ManagedSessionEvents.TriggerEffects(payload)!.Value.GetHashCode(),
            ManagedSessionEvents.TriggerEffects([.. payload])!.Value.GetHashCode());
    }

    /// <summary>
    /// PP689'S ORDER, NOW SENT: the five go out as the C sends them, not as it parsed them.
    ///
    /// That task decided the five after the pad info switch so both layouts share one sequence, and
    /// nothing sent them. This is the sequence being a thing something does: motion reset, haptic,
    /// trigger, light bar, player index - from a message whose fields sit in offset order.
    /// </summary>
    [Fact]
    public void ThePadInfoFiveGoOutInTheOrderTheCSendsThem()
    {
        byte[] narrow = new byte[PadInfoMessage.NarrowSize];
        narrow[PadInfoMessage.PlayerIndexOffset(PadInfoLayout.Narrow)] = 2;
        narrow[PadInfoMessage.LedOffset(PadInfoLayout.Narrow)] = 1;
        narrow[PadInfoMessage.LedOffset(PadInfoLayout.Narrow) + 1] = 2;
        narrow[PadInfoMessage.LedOffset(PadInfoLayout.Narrow) + 2] = 3;
        narrow[PadInfoMessage.MotionResetOffset(PadInfoLayout.Narrow)] = 1;
        narrow[PadInfoMessage.HapticOffset(PadInfoLayout.Narrow)] = (byte)DualSenseEffectIntensity.Strong;
        narrow[PadInfoMessage.TriggerOffset(PadInfoLayout.Narrow)] = (byte)DualSenseEffectIntensity.Weak;

        PadInfoReading reading = PadInfoMessage.Read(narrow, PadState.Initial);

        var events = new ManagedSessionEvents();
        var listener = new Listener();
        events.Listen(listener);

        Assert.Equal(5, events.SendPadInfo(reading));

        output.WriteLine(string.Join(", ", listener.Heard.Select(one => one.Type)));

        Assert.Equal(
            [
                ChiakiEventType.MotionReset,
                ChiakiEventType.HapticIntensity,
                ChiakiEventType.TriggerIntensity,
                ChiakiEventType.LedColor,
                ChiakiEventType.PlayerIndex,
            ],
            listener.Heard.Select(one => one.Type));

        // And each carries what the message left behind, not what was held before it.
        Assert.Equal(DualSenseEffectIntensity.Strong, listener.Heard[1].Intensity);
        Assert.Equal(DualSenseEffectIntensity.Weak, listener.Heard[2].Intensity);
        Assert.Equal(new PadLed(1, 2, 3), listener.Heard[3].Led);
        Assert.Equal(2, listener.Heard[4].PlayerIndex);
    }

    /// <summary>A message that changed nothing sends nothing, which is four of the five compared.</summary>
    [Fact]
    public void APadInfoThatChangedNothingSendsNothing()
    {
        var held = new PadState(2, new PadLed(1, 2, 3), 1, 3);

        byte[] narrow = new byte[PadInfoMessage.NarrowSize];
        narrow[PadInfoMessage.PlayerIndexOffset(PadInfoLayout.Narrow)] = held.PlayerIndex;
        narrow[PadInfoMessage.LedOffset(PadInfoLayout.Narrow)] = held.Led.Red;
        narrow[PadInfoMessage.LedOffset(PadInfoLayout.Narrow) + 1] = held.Led.Green;
        narrow[PadInfoMessage.LedOffset(PadInfoLayout.Narrow) + 2] = held.Led.Blue;
        narrow[PadInfoMessage.HapticOffset(PadInfoLayout.Narrow)] = held.HapticIntensity;
        narrow[PadInfoMessage.TriggerOffset(PadInfoLayout.Narrow)] = held.TriggerIntensity;

        var events = new ManagedSessionEvents();
        events.Listen(new Listener());

        Assert.Equal(0, events.SendPadInfo(PadInfoMessage.Read(narrow, held)));
        Assert.Equal(0, events.Sent);
    }

    /// <summary>
    /// Every one of the nine can actually be raised from here, so the list is not a wish.
    ///
    /// PP271's shape: a list of event types with no builder behind one of them would satisfy the
    /// drift check below and answer for nothing.
    /// </summary>
    [Fact]
    public void EveryEventTheFramePathRaisesCanBeBuiltHere()
    {
        SessionEvent[] built =
        [
            ManagedSessionEvents.Connected(),
            ManagedSessionEvents.Rumble(new byte[ManagedSessionEvents.RumbleMinimum])!.Value,
            ManagedSessionEvents.TriggerEffects(new byte[ManagedSessionEvents.TriggerEffectsMinimum])!.Value,
            ManagedSessionEvents.ForPad(PadReportKind.MotionReset, PadState.Initial),
            ManagedSessionEvents.ForPad(PadReportKind.HapticIntensity, PadState.Initial),
            ManagedSessionEvents.ForPad(PadReportKind.TriggerIntensity, PadState.Initial),
            ManagedSessionEvents.ForPad(PadReportKind.LedColor, PadState.Initial),
            ManagedSessionEvents.ForPad(PadReportKind.PlayerIndex, PadState.Initial),
            ManagedSessionEvents.VideoFecFailure(41, idrRequestSent: true),
        ];

        Assert.Equal(
            ManagedSessionEvents.RaisedByTheFramePath,
            built.Select(one => one.Type));
    }

    /// <summary>The FEC failure's two fields, which the video receiver's seam already carries.</summary>
    [Fact]
    public void TheFecFailureCarriesTheFrameAndWhetherAKeyframeWasAlreadyAskedFor()
    {
        SessionEvent raised = ManagedSessionEvents.VideoFecFailure(41, idrRequestSent: true);

        Assert.Equal(ChiakiEventType.VideoFecFailure, raised.Type);
        Assert.Equal(41, raised.FecFrameIndex);
        Assert.True(raised.FecIdrRequestSent);
    }

    /// <summary>
    /// THE DRIFT CHECK: the nine are what those two files raise, in that order.
    ///
    /// Read out of the C rather than trusted, and joined by the enum's own normalisation so the two
    /// spellings do not have to be maintained twice. Order rather than a set, because five of the
    /// nine leave one handler in a sequence PP689 argues IS the behaviour.
    /// </summary>
    [Fact]
    public void TheNineAreTheEventsTheFramePathStillRaises()
    {
        if (Read(ManagedSessionEventsSource.StreamRelativePath) is not { } stream
            || Read(ManagedSessionEventsSource.VideoRelativePath) is not { } video)
        {
            return;
        }

        string[] raised =
        [
            .. ManagedSessionEventsSource.EventsRaisedIn(stream),
            .. ManagedSessionEventsSource.EventsRaisedIn(video),
        ];

        output.WriteLine(string.Join(", ", raised));

        Assert.Equal(
            ManagedSessionEvents.RaisedByTheFramePath.Select(
                one => NativeEnumMirrors.Normalise(one.ToString(), ManagedSessionEventsSource.EventPrefix)),
            raised.Select(
                one => NativeEnumMirrors.Normalise(one, ManagedSessionEventsSource.EventPrefix)));
    }

    /// <summary>
    /// And the send still returns before it calls, where no callback is registered.
    ///
    /// The behaviour this seam reproduces, held against the four lines it comes from. An edit that
    /// dropped the guard would make every raiser in the frame path a null dereference, which is a
    /// crash rather than the silence a port might notice.
    /// </summary>
    [Fact]
    public void TheCsSendStillReturnsWhereNothingIsListening()
    {
        if (Read(ManagedSessionEventsSource.SessionRelativePath) is not { } session)
            return;

        string? body = ManagedSessionEventsSource.SendBody(session);
        Assert.NotNull(body);

        output.WriteLine(body.Trim());

        Assert.True(ManagedSessionEventsSource.TheSendStillReturnsWithNoCallback(body));
    }
}
