using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP684, under PP295: the four messages the stream connection could not send, and the seam that
/// carries the video receiver's two.
///
/// Three oracles, none of them this port reading its own writing: a console's own disconnect out of
/// PP396's capture, the two independent protobuf generators PP25 set against each other, and
/// PP291's receiver driven to a real failure through the first non-test implementation of its
/// outbound seam.
/// </summary>
public class StreamMessagesTests(ITestOutputHelper output)
{
    /// <summary>A sink that keeps what it was given and answers whatever it was told to.</summary>
    private sealed class Recording(bool sends = true) : IStreamMessageSink
    {
        public List<StreamMessage> Sent { get; } = [];

        public bool Send(in StreamMessage message)
        {
            Sent.Add(message);
            return sends;
        }
    }

    /// <summary>The corpus, or null outside a checkout.</summary>
    private static ExchangeRecording? Corpus()
    {
        string? path = SanitizerSource.LocateRelative(FourChannelCorpusTests.RelativePath);
        if (path is null)
            return null;

        return ExchangeRecording.Read(File.ReadAllText(path));
    }

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    private static string? Source()
        => StreamMessagesSource.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE ORACLE THAT IS NOT THIS PORT: the disconnect a real PS5 was sent, byte for byte.
    ///
    /// PP425's rule is that a payload transcribed out of a recording agrees with the recording by
    /// construction and can find nothing. This is the case where the recording is an independent
    /// witness instead: the bytes are BUILT here, from field numbers read off takion.proto, and the
    /// capture is what says the console received exactly those. It is also the entry
    /// StreamExchangeReplayTests had to scope out, because a participant driven by arrivals cannot
    /// produce a message sent at teardown.
    /// </summary>
    [Fact]
    public void TheDisconnectIsTheOneAConsoleWasSent()
    {
        if (Corpus() is not { } recording)
            return;

        StreamMessage disconnect = StreamMessages.Disconnect();
        string rendered = StreamExchangeParticipant.Render(disconnect.Body);

        output.WriteLine(rendered);

        // Both channels carry one: senkusha's and the stream's, the same bytes from the same encoder.
        List<ExchangeEntry> sent =
        [
            .. recording.Entries.Where(
                e => e.Direction == ExchangeDirection.Sent
                    && e.Payload.StartsWith(
                        $"{StreamMessages.DisconnectType:x4} ", StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(sent);
        Assert.All(sent, entry => Assert.Equal(rendered, entry.Payload));

        // And the length the C declared for its own buffer, which is the message exactly.
        Assert.Equal(26, disconnect.Body.Length);
    }

    /// <summary>
    /// Every message round-trips through protoc's generated types - the second generator, built from
    /// the same .proto as the nanopb the console is actually spoken to with.
    ///
    /// What this catches is a field number or a wire type wrong in a way that still produces bytes:
    /// the parser refuses them, or reads a different message back.
    /// </summary>
    [Fact]
    public void EveryMessageReadsBackThroughTheOtherGenerator()
    {
        foreach (StreamMessage message in StreamMessages.All)
        {
            var parsed = Tkproto.TakionMessage.Parser.ParseFrom(message.Body);

            Assert.Equal(message.PayloadType, (ushort)parsed.Type);
        }
    }

    /// <summary>
    /// And through nanopb, which is the generator the C uses - so the two halves of PP25's pair both
    /// read what these builders write.
    /// </summary>
    [Fact]
    public void EveryMessageReadsBackThroughNanopb()
    {
        foreach (StreamMessage message in StreamMessages.All)
        {
            DecodedTakionMessage? read = TakionMessages.DecodeWithNanopb(message.Body);

            Assert.NotNull(read);
            Assert.Equal(message.PayloadType, (ushort)read!.Value.Type);
        }
    }

    /// <summary>The corrupt frame's two numbers survive the round trip, which is what it is for.</summary>
    [Theory]
    [InlineData((ushort)0, (ushort)0)]
    [InlineData((ushort)1, (ushort)1)]
    [InlineData((ushort)41, (ushort)44)]
    [InlineData((ushort)0xfffe, (ushort)0xffff)]
    public void TheCorruptFrameCarriesTheFramesItNames(ushort start, ushort end)
    {
        StreamMessage message = StreamMessages.CorruptFrame(start, end);
        var parsed = Tkproto.TakionMessage.Parser.ParseFrom(message.Body);

        Assert.Equal(StreamMessages.CorruptFrameType, (ushort)parsed.Type);
        Assert.Equal(start, parsed.CorruptPayload.Start);
        Assert.Equal(end, parsed.CorruptPayload.End);
    }

    /// <summary>And the disconnect's reason, which is a required field and so is always written.</summary>
    [Fact]
    public void TheDisconnectCarriesTheReasonTheCGives()
    {
        var parsed = Tkproto.TakionMessage.Parser.ParseFrom(StreamMessages.Disconnect().Body);

        Assert.Equal(StreamMessages.DisconnectReason, parsed.DisconnectPayload.Reason);
        Assert.Equal(20, StreamMessages.DisconnectReason.Length);
    }

    /// <summary>
    /// THE DATA TYPES ARE THE C'S, read off the call sites rather than a comment beside them.
    ///
    /// This is the half a builder that produced only a body would lose. Nine for the streaminfo ack
    /// and two for the two video messages are what the console reads, and the file's own comment
    /// calls the pair "the keyboard pair", which they are not.
    /// </summary>
    [Fact]
    public void TheDataTypesAreTheOnesTheCallSitesPass()
    {
        if (Source() is not { } source)
            return;

        IReadOnlyDictionary<string, byte> declared = StreamMessagesSource.DataTypesIn(source);
        output.WriteLine(string.Join(", ", declared.Select(pair => $"{pair.Key}={pair.Value}")));

        Assert.Equal(7, declared.Count);

        Assert.Equal(StreamMessages.StreamInfoAckData, declared["STREAMINFOACK"]);
        Assert.Equal(StreamMessages.VideoData, declared["CORRUPTFRAME"]);
        Assert.Equal(StreamMessages.VideoData, declared["IDRREQUEST"]);
        Assert.Equal(StreamMessages.OrdinaryData, declared["DISCONNECT"]);
        Assert.Equal(StreamMessages.OrdinaryData, declared["HEARTBEAT"]);
        Assert.Equal(StreamMessages.OrdinaryData, declared["CONTROLLERCONNECTION"]);
        Assert.Equal(StreamMessages.OrdinaryData, declared["STREAMINFO"]);
    }

    /// <summary>
    /// And every message this builds carries the data type its own call site passes, joined by the
    /// payload type rather than by position.
    /// </summary>
    [Fact]
    public void EveryBuiltMessageCarriesItsCallSitesDataType()
    {
        if (Source() is not { } source)
            return;

        IReadOnlyDictionary<string, byte> declared = StreamMessagesSource.DataTypesIn(source);

        var names = new Dictionary<ushort, string>
        {
            [StreamMessages.HeartbeatType] = "HEARTBEAT",
            [StreamMessages.CorruptFrameType] = "CORRUPTFRAME",
            [StreamMessages.DisconnectType] = "DISCONNECT",
            [StreamMessages.IdrRequestType] = "IDRREQUEST",
            [StreamExchangeParticipant.StreamInfoAckType] = "STREAMINFOACK",
            [StreamExchangeParticipant.ControllerConnectionType] = "CONTROLLERCONNECTION",
            [StreamExchangeParticipant.StreamInfo] = "STREAMINFO",
        };

        Assert.Equal(names.Count, StreamMessages.All.Count);

        foreach (StreamMessage message in StreamMessages.All)
        {
            string name = names[message.PayloadType];
            Assert.Equal(declared[name], message.DataType);
        }
    }

    /// <summary>
    /// The payload types are the numbers takion.proto assigns, which is the one thing that is a name
    /// on one side and a number on the other.
    /// </summary>
    [Fact]
    public void ThePayloadTypesAreTheProtosOwnNumbers()
    {
        Assert.Equal(
            (ushort)Tkproto.TakionMessage.Types.PayloadType.Heartbeat, StreamMessages.HeartbeatType);
        Assert.Equal(
            (ushort)Tkproto.TakionMessage.Types.PayloadType.Corruptframe,
            StreamMessages.CorruptFrameType);
        Assert.Equal(
            (ushort)Tkproto.TakionMessage.Types.PayloadType.Disconnect, StreamMessages.DisconnectType);
        Assert.Equal(
            (ushort)Tkproto.TakionMessage.Types.PayloadType.Idrrequest, StreamMessages.IdrRequestType);
    }

    /// <summary>
    /// THE JOIN PP295'S SECOND CRITERION ASKS FOR: a real loss reaches a sink as wire bytes.
    ///
    /// The receiver is driven the way a stream drives it - a frame of two units where one never
    /// arrives, then the next frame, which is what makes the first late rather than merely
    /// incomplete. What comes out the other side is the corrupt-frame report carrying the numbers the
    /// receiver decided on, and the IDR request behind it. Nothing here says which frames were lost;
    /// the receiver does, and this reads them back off the bytes.
    ///
    /// TWO REPORTS, NOT ONE, and that is videoreceiver.c's own shape: it reports a corrupt frame at
    /// two separate places - once when a frame index arrives past the one expected, and again when a
    /// flush fails - and this scenario reaches both. A test that demanded one would be asserting a
    /// tidier receiver than the port has.
    /// </summary>
    [Fact]
    public void AFrameLostReachesTheSinkAsBothMessages()
    {
        var sink = new Recording();
        var outbound = new StreamOutbound(sink);
        var losses = new List<int>();

        var receiver = new ManagedVideoReceiver(
            (frame, framesLost, recovered) =>
            {
                losses.Add(framesLost);
                return true;
            },
            outbound,
            idrOnFecFailure: true);

        receiver.StreamInfo(new byte[] { 0, 0, 0, 0 });

        // One frame of two units where only the first arrives, then the next frame - which is what
        // makes the first one late rather than merely incomplete.
        receiver.AvPacket(frameIndex: 1, unitIndex: 0, total: 2, fec: 0, [1, 2, 3, 4]);
        receiver.AvPacket(frameIndex: 2, unitIndex: 0, total: 1, fec: 0, [5, 6, 7, 8]);

        output.WriteLine(
            string.Join(", ", sink.Sent.Select(m => $"{m.PayloadType}/{m.DataType}")));

        List<StreamMessage> corrupt =
            [.. sink.Sent.Where(m => m.PayloadType == StreamMessages.CorruptFrameType)];

        // The gap report and the flush-failure report, which are videoreceiver.c's two call sites.
        Assert.Equal(2, corrupt.Count);

        foreach (StreamMessage report in corrupt)
        {
            var parsed = Tkproto.TakionMessage.Parser.ParseFrom(report.Body);

            Assert.Equal(StreamMessages.VideoData, report.DataType);
            Assert.True(
                parsed.CorruptPayload.End >= parsed.CorruptPayload.Start,
                $"a report runs backwards: {parsed.CorruptPayload.Start} to {parsed.CorruptPayload.End}");
        }

        // And the keyframe ask behind them, which is what idrOnFecFailure buys.
        StreamMessage idr = Assert.Single(
            sink.Sent, m => m.PayloadType == StreamMessages.IdrRequestType);
        Assert.Equal(StreamMessages.VideoData, idr.DataType);

        // The receiver counted the loss, so the reports are about a frame it decided was gone.
        Assert.True(receiver.FramesLostTotal > 0);
        Assert.Equal(1, outbound.FecFailures);
    }

    /// <summary>
    /// THE IDR REQUEST'S ANSWER IS LOAD-BEARING, so the seam hands the sink's back.
    ///
    /// ManagedVideoReceiver only starts waiting for a keyframe where the request went out. A seam
    /// that answered true regardless would leave it dropping frames while waiting for an IDR nobody
    /// asked for, which is a stall with no error anywhere.
    /// </summary>
    [Fact]
    public void TheIdrRequestHandsBackWhetherItWent()
    {
        Assert.True(new StreamOutbound(new Recording(sends: true)).SendIdrRequest());
        Assert.False(new StreamOutbound(new Recording(sends: false)).SendIdrRequest());
    }

    /// <summary>The corrupt frame the seam sends is the pair it was given, unchanged.</summary>
    [Fact]
    public void TheSeamSendsTheFramesItWasGiven()
    {
        var sink = new Recording();
        new StreamOutbound(sink).SendCorruptFrame(41, 44);

        StreamMessage sent = Assert.Single(sink.Sent);
        var parsed = Tkproto.TakionMessage.Parser.ParseFrom(sent.Body);

        Assert.Equal(41u, parsed.CorruptPayload.Start);
        Assert.Equal(44u, parsed.CorruptPayload.End);
    }

    /// <summary>
    /// An FEC failure is counted and NOT sent, because the C logs it and the corrupt-frame report
    /// beside it is what crosses the wire.
    /// </summary>
    [Fact]
    public void AnFecFailureIsCountedRatherThanSent()
    {
        var sink = new Recording();
        var outbound = new StreamOutbound(sink);

        outbound.FecFailure(7, idrRequestSent: true);

        Assert.Empty(sink.Sent);
        Assert.Equal(1, outbound.FecFailures);
        Assert.Equal(7, outbound.LastFecFailure);
    }

    /// <summary>A sink is required, because a seam with nowhere to send is a silent drop.</summary>
    [Fact]
    public void ASinkIsRequired()
        => Assert.Throws<ArgumentNullException>(() => new StreamOutbound(null!));

    /// <summary>
    /// The three PP424 built are not rebuilt here: the bytes are the same object, so the table is
    /// complete without a second copy of any message to drift from the first.
    /// </summary>
    [Fact]
    public void TheThreeThatExistedAreNotRebuilt()
    {
        Assert.Equal(StreamExchangeParticipant.StreamInfoAck(), StreamMessages.StreamInfoAck().Body);
        Assert.Equal(
            StreamExchangeParticipant.ControllerConnection(true),
            StreamMessages.ControllerConnection(true).Body);
        Assert.Equal(
            StreamExchangeParticipant.MicrophoneStreamInfo(),
            StreamMessages.MicrophoneStreamInfo().Body);
    }

    /// <summary>PP272: the reader says no about nothing, rather than an empty table that reads as agreement.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
        => Assert.Empty(StreamMessagesSource.DataTypesIn(""));
}
