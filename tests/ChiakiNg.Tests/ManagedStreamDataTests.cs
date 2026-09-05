using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP721: stream_connection_takion_data - the join between PP366's dispatch and PP719's seam.
///
/// Both ends existed and nothing connected them. PP689 read a pad info message and returned five
/// reports; PP719 built the events those reports become; the rumble and trigger-effects parses had
/// no caller at all, because nothing routed a data message to them.
///
/// THE ASSERTION THIS FILE EXISTS FOR is that the pad state is HELD. Four of the five reports are
/// comparisons against what the console last said, so the same message delivered twice must decide
/// five things and then nothing - and a port keeping that state anywhere but here would report five
/// changes per message and rewrite the light bar as fast as the console sends.
/// </summary>
public class ManagedStreamDataTests(ITestOutputHelper output)
{
    /// <summary>Every event that reached a sink, in order.</summary>
    private sealed class Listener : ISessionEventSink
    {
        private readonly List<SessionEvent> heard = [];

        public IReadOnlyList<SessionEvent> Heard => heard;

        public void Send(in SessionEvent raised) => heard.Add(raised);
    }

    /// <summary>A message whose sink records, so a delivery can be read rather than counted.</summary>
    private sealed class Sends : IStreamMessageSink
    {
        public int Count { get; private set; }

        public bool Send(in StreamMessage message)
        {
            Count++;
            return true;
        }
    }

    private static (ManagedStreamData Data, Listener Heard) Wired()
    {
        var events = new ManagedSessionEvents();
        var listener = new Listener();
        events.Listen(listener);

        return (new ManagedStreamData(events), listener);
    }

    /// <summary>A narrow pad info, which is the shorter of the two layouts.</summary>
    private static byte[] PadInfo(byte playerIndex, PadLed led, byte haptic, byte trigger, bool motionReset = false)
    {
        byte[] message = new byte[PadInfoMessage.NarrowSize];
        const PadInfoLayout narrow = PadInfoLayout.Narrow;

        message[PadInfoMessage.PlayerIndexOffset(narrow)] = playerIndex;
        message[PadInfoMessage.LedOffset(narrow)] = led.Red;
        message[PadInfoMessage.LedOffset(narrow) + 1] = led.Green;
        message[PadInfoMessage.LedOffset(narrow) + 2] = led.Blue;
        message[PadInfoMessage.MotionResetOffset(narrow)] = motionReset ? (byte)1 : (byte)0;
        message[PadInfoMessage.HapticOffset(narrow)] = haptic;
        message[PadInfoMessage.TriggerOffset(narrow)] = trigger;

        return message;
    }

    private static string? Read()
    {
        string? path = ManagedStreamDataSource.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE STATE IS HELD: the same message twice decides five things and then nothing.
    ///
    /// The whole reason this is an object. A port that parsed each message on its own would send
    /// the console five events for every pad info it receives, several times a second, and the
    /// light bar would be rewritten from a value that had not moved.
    /// </summary>
    [Fact]
    public void TheSamePadInfoTwiceDecidesFiveThingsAndThenOne()
    {
        (ManagedStreamData data, Listener heard) = Wired();
        byte[] message = PadInfo(2, new PadLed(1, 2, 3), 1, 3, motionReset: true);

        StreamDataOutcome first = data.Deliver(TakionDataType.PadInfo, message);
        StreamDataOutcome again = data.Deliver(TakionDataType.PadInfo, message);

        output.WriteLine($"{first.Events} then {again.Events}, {heard.Heard.Count} heard");

        Assert.Equal(5, first.Events);
        Assert.Equal(StreamDataResult.Raised, first.Result);

        // The motion reset is not a comparison and would fire again; it is zero here because the
        // second message is the same one and its byte is read, not remembered.
        Assert.Equal(1, again.Events);
        Assert.Equal(StreamDataResult.Raised, again.Result);
        Assert.Equal(ChiakiEventType.MotionReset, heard.Heard[^1].Type);
    }

    /// <summary>
    /// And with the ask cleared, a repeat decides nothing at all.
    ///
    /// The four comparisons on their own. This is the ordinary case on a live stream: the console
    /// sends pad info continuously and almost none of it has changed.
    /// </summary>
    [Fact]
    public void APadInfoRepeatingWhatIsHeldDecidesNothing()
    {
        (ManagedStreamData data, Listener heard) = Wired();
        byte[] message = PadInfo(2, new PadLed(1, 2, 3), 1, 3);

        Assert.Equal(4, data.Deliver(TakionDataType.PadInfo, message).Events);

        StreamDataOutcome again = data.Deliver(TakionDataType.PadInfo, message);

        Assert.Equal(0, again.Events);
        Assert.Equal(StreamDataResult.NothingToSay, again.Result);
        Assert.Equal(4, heard.Heard.Count);
    }

    /// <summary>The five go out in PP689's order, through the seam PP719 built.</summary>
    [Fact]
    public void TheFiveReachTheSeamInTheCsOrder()
    {
        (ManagedStreamData data, Listener heard) = Wired();

        data.Deliver(TakionDataType.PadInfo, PadInfo(2, new PadLed(1, 2, 3), 1, 3, motionReset: true));

        Assert.Equal(
            [
                ChiakiEventType.MotionReset,
                ChiakiEventType.HapticIntensity,
                ChiakiEventType.TriggerIntensity,
                ChiakiEventType.LedColor,
                ChiakiEventType.PlayerIndex,
            ],
            heard.Heard.Select(one => one.Type));
    }

    /// <summary>
    /// A length the C refuses leaves the held state alone, so the message after it is judged fairly.
    ///
    /// The failure this rules out is a refusal that clears the state: the next ordinary message
    /// would then report five changes against nothing, which is the same defect as not holding it.
    /// </summary>
    [Fact]
    public void ALengthTheCRefusesLeavesTheStateAlone()
    {
        (ManagedStreamData data, Listener heard) = Wired();
        byte[] message = PadInfo(2, new PadLed(1, 2, 3), 1, 3);

        Assert.Equal(4, data.Deliver(TakionDataType.PadInfo, message).Events);

        StreamDataOutcome refused = data.Deliver(TakionDataType.PadInfo, new byte[7]);

        Assert.Equal(StreamDataResult.Refused, refused.Result);
        Assert.Equal(1, data.Refused);

        Assert.Equal(0, data.Deliver(TakionDataType.PadInfo, message).Events);
        Assert.Equal(4, heard.Heard.Count);
    }

    /// <summary>A rumble reaches the seam as its three bytes.</summary>
    [Fact]
    public void ARumbleReachesTheSeam()
    {
        (ManagedStreamData data, Listener heard) = Wired();

        StreamDataOutcome outcome = data.Deliver(TakionDataType.Rumble, [0x11, 0x22, 0x33]);

        Assert.Equal(TakionData.Rumble, outcome.Kind);
        Assert.Equal(StreamDataResult.Raised, outcome.Result);
        Assert.Equal(
            new RumbleState(0x11, 0x22, 0x33),
            Assert.Single(heard.Heard).Rumble);
    }

    /// <summary>And trigger effects do, at the offsets the C reads them from.</summary>
    [Fact]
    public void TriggerEffectsReachTheSeam()
    {
        (ManagedStreamData data, Listener heard) = Wired();

        byte[] payload = new byte[ManagedSessionEvents.TriggerEffectsMinimum];
        payload[ManagedSessionEvents.TriggerTypeLeftOffset] = 0x21;

        Assert.Equal(StreamDataResult.Raised, data.Deliver(TakionDataType.TriggerEffects, payload).Result);
        Assert.Equal(0x21, Assert.Single(heard.Heard).TriggerEffects.TypeLeft);
    }

    /// <summary>Each handler's own size guard refuses without raising, and the refusal is counted.</summary>
    [Theory]
    [InlineData(TakionDataType.Rumble, 2)]
    [InlineData(TakionDataType.TriggerEffects, 24)]
    public void AShortPayloadIsRefusedAndRaisesNothing(TakionDataType type, int length)
    {
        (ManagedStreamData data, Listener heard) = Wired();

        StreamDataOutcome outcome = data.Deliver(type, new byte[length]);

        Assert.Equal(StreamDataResult.Refused, outcome.Result);
        Assert.Equal(0, outcome.Events);
        Assert.Empty(heard.Heard);
        Assert.Equal(1, data.Refused);
    }

    /// <summary>The protobuf arm stops here: which handler it reaches is the state's question.</summary>
    [Fact]
    public void TheProtobufArmStopsAtThisLayer()
    {
        (ManagedStreamData data, Listener heard) = Wired();

        StreamDataOutcome outcome = data.Deliver(TakionDataType.Protobuf, [0x08, 0x03]);

        Assert.Equal(StreamDataResult.ToProtobuf, outcome.Result);
        Assert.Empty(heard.Heard);
        Assert.Equal(0, data.Decided);
    }

    /// <summary>A fifth data type is dropped without a word, which is the C's default arm.</summary>
    [Fact]
    public void AnUnknownTypeIsDroppedSilently()
    {
        (ManagedStreamData data, Listener heard) = Wired();

        StreamDataOutcome outcome = data.Deliver((TakionDataType)99, [1, 2, 3, 4, 5]);

        Assert.Equal(TakionData.Other, outcome.Kind);
        Assert.Equal(StreamDataResult.Dropped, outcome.Result);
        Assert.Empty(heard.Heard);
        Assert.Equal(0, data.Refused);
    }

    /// <summary>
    /// PP721: the FEC failure now reaches the seam, and still crosses no wire.
    ///
    /// The ninth of the frame path's events and the only one videoreceiver.c raises. The seam's two
    /// arguments were already the event's two fields; what was missing was somewhere to put them.
    /// </summary>
    [Fact]
    public void TheFecFailureReachesTheSeamAndSendsNoMessage()
    {
        var events = new ManagedSessionEvents();
        var listener = new Listener();
        events.Listen(listener);

        var sends = new Sends();
        var outbound = new StreamOutbound(sends, events);

        outbound.FecFailure(41, idrRequestSent: true);

        SessionEvent raised = Assert.Single(listener.Heard);

        Assert.Equal(ChiakiEventType.VideoFecFailure, raised.Type);
        Assert.Equal(41, raised.FecFrameIndex);
        Assert.True(raised.FecIdrRequestSent);

        // And nothing went out on the wire for it, which was true before PP721 and still is.
        Assert.Equal(0, sends.Count);
        Assert.Equal(1, outbound.FecFailures);
    }

    /// <summary>With no seam it counts and raises nothing, which is every caller before PP721.</summary>
    [Fact]
    public void AnOutboundWithNoSeamStillOnlyCounts()
    {
        var sends = new Sends();
        var outbound = new StreamOutbound(sends);

        outbound.FecFailure(7, idrRequestSent: false);

        Assert.Equal(1, outbound.FecFailures);
        Assert.Equal(7, outbound.LastFecFailure);
        Assert.Equal(0, sends.Count);
    }

    /// <summary>
    /// THE DRIFT CHECK: the four types still reach the four handlers, and none of them swapped.
    ///
    /// Read as the pairing rather than as a list of names. Two arms exchanged would be a rumble
    /// parsed as a pad info - a length that happens to be refused, and then one that is not.
    /// </summary>
    [Fact]
    public void TheFourTypesStillReachTheFourHandlers()
    {
        if (Read() is not { } source)
            return;

        string? body = ManagedStreamDataSource.SwitchBody(source);
        Assert.NotNull(body);

        IReadOnlyDictionary<string, string> handlers = ManagedStreamDataSource.HandlersIn(body);

        output.WriteLine(string.Join(", ", handlers.Select(one => $"{one.Key}->{one.Value}")));

        Assert.Equal(4, handlers.Count);
        Assert.Equal("stream_connection_takion_data_protobuf", handlers["PROTOBUF"]);
        Assert.Equal("stream_connection_takion_data_rumble", handlers["RUMBLE"]);
        Assert.Equal("stream_connection_takion_data_pad_info", handlers["PAD_INFO"]);
        Assert.Equal("stream_connection_takion_data_trigger_effects", handlers["TRIGGER_EFFECTS"]);

        Assert.True(
            ManagedStreamDataSource.AnUnknownTypeIsStillDroppedSilently(body),
            "an unrecognised data type is no longer dropped without a word");
    }

    /// <summary>And this port's own mapping names the same four kinds.</summary>
    [Theory]
    [InlineData(TakionDataType.Protobuf, TakionData.Protobuf)]
    [InlineData(TakionDataType.Rumble, TakionData.Rumble)]
    [InlineData(TakionDataType.PadInfo, TakionData.PadInfo)]
    [InlineData(TakionDataType.TriggerEffects, TakionData.TriggerEffects)]
    public void TheWireTypeMapsToTheKindTheDispatchSwitchesOn(TakionDataType type, TakionData kind)
        => Assert.Equal(kind, ManagedStreamData.KindOf(type));
}
