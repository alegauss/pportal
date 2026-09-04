using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP686, under PP295: the console's STREAMINFO read, and the receiver handed what it announced.
///
/// The subject is PP396's capture - the message a PS5 actually sent - so what the parse yields is
/// judged against a console rather than against a message this port composed. PP372's two repairs
/// are held beside it, each by a case that fails without the rule.
/// </summary>
public class StreamInfoMessageTests(ITestOutputHelper output)
{
    /// <summary>The console's own STREAMINFO out of the capture, or null outside a checkout.</summary>
    private static byte[]? FromTheConsole()
    {
        string? path = SanitizerSource.LocateRelative(FourChannelCorpusTests.RelativePath);
        if (path is null)
            return null;

        ExchangeRecording? recording = ExchangeRecording.Read(File.ReadAllText(path));
        if (recording is null)
            return null;

        // Received, on the stream channel, and the STREAMINFO rather than this side's answer.
        ExchangeEntry? entry = recording.Entries.FirstOrDefault(
            e => e.Direction == ExchangeDirection.Received
                && e.Channel == ChiakiMessageTap.StreamChannel
                && e.Payload.StartsWith(
                    $"{StreamExchangeParticipant.StreamInfo:x4} ", StringComparison.Ordinal));

        return entry is null ? null : Bytes(entry.Value.Payload);
    }

    /// <summary>A recorded payload's bytes, as the recording renders them.</summary>
    private static byte[] Bytes(string payload)
        => [.. payload[5..]
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => Convert.ToByte(pair, 16))];

    /// <summary>A streaminfo built here, for the cases a capture cannot supply.</summary>
    private static byte[] Built(int resolutions, int audioHeaderSize, int headerSize = 8)
    {
        var message = new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Streaminfo,
            StreamInfoPayload = new Tkproto.StreamInfoPayload
            {
                AudioHeader = ByteString.CopyFrom(new byte[audioHeaderSize]),
            },
        };

        for (var i = 0; i < resolutions; i++)
        {
            message.StreamInfoPayload.Resolution.Add(new Tkproto.ResolutionPayload
            {
                Width = (uint)(1280 + i),
                Height = (uint)(720 + i),

                // A header whose first byte names it, so the order the profiles come back in is
                // checkable rather than assumed.
                VideoHeader = ByteString.CopyFrom(
                    [.. Enumerable.Repeat((byte)i, headerSize)]),
            });
        }

        return message.ToByteArray();
    }

    /// <summary>
    /// THE CAPTURE: what a PS5 announced, read.
    ///
    /// Nothing here says how many resolutions or how big a header; the console did, and this reports
    /// what it said. What is asserted is the shape a stream needs to start - profiles with real
    /// pixel counts, each carrying a header, and the one audio header at the size the receiver loads.
    /// </summary>
    [Fact]
    public void TheConsolesOwnStreamInfoIsRead()
    {
        if (FromTheConsole() is not { } message)
            return;

        StreamInfoReading reading = StreamInfoMessage.Read(message);

        Assert.Equal(StreamInfoVerdict.Accepted, reading.Verdict);
        Assert.NotEmpty(reading.Profiles);
        Assert.Equal(StreamInfoMessage.AudioHeaderSize, reading.AudioHeader!.Length);

        foreach (VideoProfile profile in reading.Profiles)
        {
            output.WriteLine(
                $"{profile.Width}x{profile.Height}, header {profile.HeaderLength} bytes");

            Assert.True(profile.Width > 0 && profile.Height > 0);
            Assert.True(profile.HeaderLength > 0, "a profile arrived with no codec header");
            Assert.Equal(profile.HeaderLength + StreamInfoMessage.PaddingBytes, profile.Header.Length);
        }

        // The console announced no more than the port has room for, so nothing was capped away here.
        Assert.Equal(reading.Announced, reading.Profiles.Count);
    }

    /// <summary>
    /// And every header it sent is padded, with the padding zero - which is what the decoder reads
    /// into past the end of what the console sent.
    /// </summary>
    [Fact]
    public void EveryHeaderFromTheConsoleCarriesItsPadding()
    {
        if (FromTheConsole() is not { } message)
            return;

        StreamInfoReading reading = StreamInfoMessage.Read(message);

        foreach (VideoProfile profile in reading.Profiles)
        {
            Assert.All(
                profile.Header[profile.HeaderLength..],
                b => Assert.Equal(0, b));
        }
    }

    /// <summary>
    /// PP372'S FIRST REPAIR: the cap comes before the padding, so a ninth profile is not kept.
    ///
    /// The console chooses how many it announces. Below the cap, the C padded one per announced
    /// resolution and kept the first eight; the count the console gave is reported separately so the
    /// discard is visible rather than silent.
    /// </summary>
    [Fact]
    public void ANinthResolutionIsNeitherKeptNorPadded()
    {
        StreamInfoReading reading = StreamInfoMessage.Read(
            Built(resolutions: 11, audioHeaderSize: StreamInfoMessage.AudioHeaderSize));

        Assert.Equal(StreamInfoVerdict.Accepted, reading.Verdict);
        Assert.Equal(StreamInfoMessage.ProfilesMax, reading.Profiles.Count);
        Assert.Equal(11, reading.Announced);

        // The first eight, in the order the console announced them.
        Assert.Equal(
            Enumerable.Range(0, StreamInfoMessage.ProfilesMax),
            reading.Profiles.Select(p => (int)p.Header[0]));
    }

    /// <summary>Exactly eight is not a ninth, so the cap does not fire one profile early.</summary>
    [Fact]
    public void ExactlyEightIsKept()
    {
        StreamInfoReading reading = StreamInfoMessage.Read(
            Built(resolutions: StreamInfoMessage.ProfilesMax,
                audioHeaderSize: StreamInfoMessage.AudioHeaderSize));

        Assert.Equal(StreamInfoMessage.ProfilesMax, reading.Profiles.Count);
        Assert.Equal(reading.Announced, reading.Profiles.Count);
    }

    /// <summary>
    /// PP372'S SECOND: a SHORT audio header refuses the message, and the resolutions are already
    /// read by then.
    ///
    /// That second half is the reason the leak was a task rather than a note - a console with a bad
    /// audio header got here having had every resolution decoded and padded.
    ///
    /// PP687 CORRECTED THE OTHER HALF OF THIS CASE. It named fifteen and sixty-four too, and both
    /// were wrong: the C's bounded read refuses a field past its maximum instead of truncating it, so
    /// a long header fails the decode and never reaches the size check. Those two moved to the theory
    /// below, which is what asserts the difference.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13)]
    public void AShortAudioHeaderRefusesTheMessageForItsSize(int size)
    {
        StreamInfoReading reading = StreamInfoMessage.Read(Built(resolutions: 3, audioHeaderSize: size));

        Assert.Equal(StreamInfoVerdict.AudioHeaderWrongSize, reading.Verdict);
        Assert.Equal(size, reading.AudioHeader!.Length);

        // Already decoded, which is what the refusal has to leave behind.
        Assert.Equal(3, reading.Profiles.Count);
    }

    /// <summary>
    /// PP687: and a LONG one is no message at all, because the bounded read refuses rather than
    /// truncating - so the handler never reaches the check that would call it a wrong size.
    ///
    /// Fifteen is the case that tells the two apart, one byte past a bound that is also the only
    /// accepted length. A port that truncated would call it accepted and hand the audio receiver
    /// fourteen bytes of a header the console did not send.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(64)]
    public void ALongAudioHeaderIsNoMessageAtAll(int size)
    {
        StreamInfoReading reading = StreamInfoMessage.Read(Built(resolutions: 3, audioHeaderSize: size));

        Assert.Equal(StreamInfoVerdict.Undecodable, reading.Verdict);
    }

    /// <summary>Fourteen is what the audio receiver loads, and the only size accepted.</summary>
    [Fact]
    public void FourteenIsTheOnlySizeAccepted()
    {
        Assert.Equal(
            StreamInfoVerdict.Accepted,
            StreamInfoMessage.Read(
                Built(resolutions: 1, audioHeaderSize: StreamInfoMessage.AudioHeaderSize)).Verdict);

        // The boundary from both sides, which is the whole of PP687's correction.
        Assert.Equal(
            StreamInfoVerdict.AudioHeaderWrongSize,
            StreamInfoMessage.Read(
                Built(resolutions: 1, audioHeaderSize: StreamInfoMessage.AudioHeaderSize - 1)).Verdict);
        Assert.Equal(
            StreamInfoVerdict.Undecodable,
            StreamInfoMessage.Read(
                Built(resolutions: 1, audioHeaderSize: StreamInfoMessage.AudioHeaderSize + 1)).Verdict);
    }

    /// <summary>
    /// A DISCONNECT arriving where a streaminfo was expected is a disconnect, not an unknown message
    /// - which is a console hanging up during setup, told apart from one talking nonsense.
    /// </summary>
    [Fact]
    public void ADisconnectHereIsADisconnect()
    {
        StreamInfoReading reading = StreamInfoMessage.Read(StreamMessages.Disconnect().Body);

        Assert.Equal(StreamInfoVerdict.Disconnect, reading.Verdict);
        Assert.Empty(reading.Profiles);
    }

    /// <summary>Any other message is refused as itself, and bytes that are not a protobuf as that.</summary>
    [Fact]
    public void SomethingElseAndSomethingBrokenAreDifferentAnswers()
    {
        Assert.Equal(
            StreamInfoVerdict.NotStreamInfo,
            StreamInfoMessage.Read(StreamMessages.Heartbeat().Body).Verdict);

        // A length that runs off the end of the message: not a protobuf at all.
        Assert.Equal(
            StreamInfoVerdict.Undecodable,
            StreamInfoMessage.Read([0x7a, 0x7f, 0x01, 0x02]).Verdict);

        Assert.Equal(StreamInfoVerdict.NotStreamInfo, StreamInfoMessage.Read([]).Verdict);
    }

    /// <summary>A resolution whose header did not decode is skipped and costs no room.</summary>
    [Fact]
    public void AResolutionWithNoHeaderIsSkipped()
    {
        var message = new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Streaminfo,
            StreamInfoPayload = new Tkproto.StreamInfoPayload
            {
                AudioHeader = ByteString.CopyFrom(new byte[StreamInfoMessage.AudioHeaderSize]),
            },
        };

        message.StreamInfoPayload.Resolution.Add(new Tkproto.ResolutionPayload
        {
            Width = 1280, Height = 720, VideoHeader = ByteString.Empty,
        });
        message.StreamInfoPayload.Resolution.Add(new Tkproto.ResolutionPayload
        {
            Width = 1920, Height = 1080, VideoHeader = ByteString.CopyFrom([1, 2, 3]),
        });

        StreamInfoReading reading = StreamInfoMessage.Read(message.ToByteArray());

        VideoProfile kept = Assert.Single(reading.Profiles);
        Assert.Equal(1920u, kept.Width);
        Assert.Equal(2, reading.Announced);
    }

    /// <summary>
    /// THE HANDOVER, WHICH IS PP295'S SECOND CRITERION: the receiver takes the console's own
    /// profiles, and a packet on one of them reaches the handler behind that profile's header.
    ///
    /// The header goes to the handler as a frame-shaped thing that is not a frame, which is what a
    /// decoder needs before any picture. Before this the receiver had only ever been handed bytes a
    /// test wrote, so what it switched between was a fixture.
    /// </summary>
    [Fact]
    public void TheReceiverIsHandedTheProfilesTheConsoleAnnounced()
    {
        if (FromTheConsole() is not { } message)
            return;

        StreamInfoReading reading = StreamInfoMessage.Read(message);
        Assert.Equal(StreamInfoVerdict.Accepted, reading.Verdict);

        var handed = new List<byte[]>();
        var sink = new Recording();

        var receiver = new ManagedVideoReceiver(
            (frame, lost, recovered) =>
            {
                handed.Add(frame.ToArray());
                return true;
            },
            new StreamOutbound(sink));

        receiver.StreamInfo(StreamInfoMessage.HeadersFor(reading));

        // One unit on the first profile: the header goes ahead of it.
        receiver.AvPacket(
            frameIndex: 1, unitIndex: 0, total: 1, fec: 0, [9, 9, 9, 9], adaptiveStreamIndex: 0);

        Assert.NotEmpty(handed);
        Assert.Equal(reading.Profiles[0].Header, handed[0]);
    }

    /// <summary>A sink that keeps what it is given, so the receiver has somewhere to report to.</summary>
    private sealed class Recording : IStreamMessageSink
    {
        public List<StreamMessage> Sent { get; } = [];

        public bool Send(in StreamMessage message)
        {
            Sent.Add(message);
            return true;
        }
    }

    /// <summary>The two constants are the C's defines, not numbers typed here.</summary>
    [Fact]
    public void TheConstantsAreTheCs()
    {
        if (StreamInfoMessageSource.Locate(StreamInfoMessageSource.AudioHeaderRelativePath)
                is not { } audioPath
            || StreamInfoMessageSource.Locate(StreamInfoMessageSource.VideoHeaderRelativePath)
                is not { } videoPath)
        {
            return;
        }

        Assert.Equal(
            (long?)StreamInfoMessage.AudioHeaderSize,
            StreamInfoMessageSource.AudioHeaderSizeIn(File.ReadAllText(audioPath)));
        Assert.Equal(
            (long?)StreamInfoMessage.PaddingBytes,
            StreamInfoMessageSource.PaddingIn(File.ReadAllText(videoPath)));
    }

    /// <summary>And the C still refuses in the order this reads.</summary>
    [Fact]
    public void TheCStillRefusesInThisOrder()
    {
        if (StreamInfoMessageSource.Locate(StreamInfoMessageSource.RelativePath) is not { } path)
            return;

        string? body = StreamInfoMessageSource.HandlerBody(File.ReadAllText(path));
        Assert.NotNull(body);

        Assert.True(
            StreamInfoMessageSource.ADisconnectIsStillRoutedFirst(body!),
            "a DISCONNECT arriving where a streaminfo was expected is no longer routed, or is no "
                + "longer tested before the generic refusal - so a hang-up during setup now reads as "
                + "an unknown message");

        Assert.True(
            StreamInfoMessageSource.TheAudioSizeIsCheckedBeforeEitherHandover(body!),
            "the audio header's size is no longer checked before the receivers are handed anything, "
                + "so a wrong size is now a bad load rather than a refusal");
    }

    /// <summary>
    /// PP372's ordering, still read from the file this parse is a port of - because the cap coming
    /// before the padding is the rule, and the managed side keeps it as which profiles survive.
    /// </summary>
    [Fact]
    public void TheCapStillComesBeforeThePaddingInTheC()
    {
        if (StreamInfoMessageSource.Locate(StreamInfoMessageSource.RelativePath) is not { } path)
            return;

        Assert.True(
            VideoProfileOwnershipSource.TheCountIsCheckedBeforeTheHeaderIsPadded(
                File.ReadAllText(path)));
    }

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(StreamInfoMessageSource.HandlerBody(""));
        Assert.Null(StreamInfoMessageSource.AudioHeaderSizeIn(""));
        Assert.Null(StreamInfoMessageSource.PaddingIn(""));
        Assert.False(StreamInfoMessageSource.ADisconnectIsStillRoutedFirst(""));
        Assert.False(StreamInfoMessageSource.TheAudioSizeIsCheckedBeforeEitherHandover(""));
    }
}
