using System.Text;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP342, under PP294: what the control channel sends when something arrives, and in what order.
///
/// §PP294 says the risk plainly - "a table of message-in, message-out pairs would pass while
/// missing the ordering entirely" - so the ordering is what most of this file is about, and
/// PP297's capture is what it is held against.
/// </summary>
public class CtrlReactionsTests
{
    private static IReadOnlyList<(long At, ExchangeDirection Way, ushort Type)> RecordedCtrl()
    {
        string? path = SanitizerSource.LocateRelative(ExchangeCorpusTests.RelativePath);
        if (path is null)
            return [];

        ExchangeRecording? recording = ExchangeRecording.Read(File.ReadAllText(path));
        if (recording is null)
            return [];

        var seen = new List<(long, ExchangeDirection, ushort)>();
        foreach (ExchangeEntry entry in recording.Entries.Where(e => e.Channel == "ctrl"))
        {
            if (ushort.TryParse(
                    entry.Payload.AsSpan(0, 4), System.Globalization.NumberStyles.HexNumber, null,
                    out ushort type))
            {
                seen.Add((entry.AtMicroseconds, entry.Direction, type));
            }
        }

        return seen;
    }

    /// <summary>A heartbeat request is answered with a reply and nothing else.</summary>
    [Fact]
    public void AHeartbeatIsAnsweredWithAReply()
    {
        IReadOnlyList<ushort> answer = CtrlReactions.Answer(
            (ushort)CtrlMessage.HeartbeatReq, new CtrlFeatures(), new CtrlSeen());

        Assert.Equal([(ushort)CtrlMessage.HeartbeatRep], answer);
    }

    /// <summary>
    /// AND IN THE CAPTURE IT IS ANSWERED AT ONCE - three times, none of them slower than 40µs.
    ///
    /// Nothing may sit between the arrival and the reply. A port that queued the reply behind
    /// anything else would still pass a pair table and would still be a different client.
    /// </summary>
    [Fact]
    public void EveryRecordedHeartbeatIsAnsweredImmediately()
    {
        IReadOnlyList<(long At, ExchangeDirection Way, ushort Type)> ctrl = RecordedCtrl();
        if (ctrl.Count == 0)
            return;

        var answered = 0;
        for (var i = 0; i < ctrl.Count; i++)
        {
            if (ctrl[i].Type != (ushort)CtrlMessage.HeartbeatReq)
                continue;

            Assert.True(i + 1 < ctrl.Count, "a recorded heartbeat was never answered");

            (long at, ExchangeDirection way, ushort type) = ctrl[i + 1];

            Assert.Equal((ushort)CtrlMessage.HeartbeatRep, type);
            Assert.Equal(ExchangeDirection.Sent, way);
            Assert.True(
                at - ctrl[i].At < 1000,
                $"the reply came {at - ctrl[i].At}us after the request, which is not immediately");

            answered++;
        }

        Assert.Equal(3, answered);
    }

    /// <summary>
    /// A SESSION ID IS NOT ANSWERED, IT IS ACTED ON - and the burst is what the capture shows.
    ///
    /// The capture was taken with DualSense and keyboard both off, so what follows the session id
    /// is the unconditional tail of ctrl_enable_features: two microphone toggles and a
    /// display-devices, in that order.
    /// </summary>
    [Fact]
    public void ASessionIdIsFollowedByTheBurstTheCaptureShows()
    {
        IReadOnlyList<ushort> burst = CtrlReactions.Answer(
            (ushort)CtrlMessage.SessionId, new CtrlFeatures(), new CtrlSeen());

        Assert.Equal(
            [(ushort)CtrlMessage.MicToggle, (ushort)CtrlMessage.MicToggle,
             (ushort)CtrlMessage.DisplayDevices],
            burst);
    }

    /// <summary>And the recording agrees, message for message and in order.</summary>
    [Fact]
    public void TheRecordedBurstIsTheOneTheModelProduces()
    {
        IReadOnlyList<(long At, ExchangeDirection Way, ushort Type)> ctrl = RecordedCtrl();
        if (ctrl.Count == 0)
            return;

        int id = -1;
        for (var i = 0; i < ctrl.Count; i++)
        {
            if (ctrl[i].Type == (ushort)CtrlMessage.SessionId)
            {
                id = i;
                break;
            }
        }

        Assert.True(id >= 0, "the capture holds no session id");

        IReadOnlyList<ushort> expected = CtrlReactions.EnableFeatures(new CtrlFeatures());

        for (var n = 0; n < expected.Count; n++)
        {
            (_, ExchangeDirection way, ushort type) = ctrl[id + 1 + n];

            Assert.Equal(ExchangeDirection.Sent, way);
            Assert.Equal(expected[n], type);
        }
    }

    /// <summary>
    /// The microphone is toggled TWICE, which is the ordering detail a pair table loses.
    ///
    /// Two identical sends, 108 microseconds apart in the capture. A port that sent one would be a
    /// different client and no in/out mapping would say so.
    /// </summary>
    [Fact]
    public void TheMicrophoneIsToggledTwice()
    {
        IReadOnlyList<ushort> burst = CtrlReactions.EnableFeatures(new CtrlFeatures());

        Assert.Equal(2, burst.Count(t => t == (ushort)CtrlMessage.MicToggle));
    }

    /// <summary>The conditional pairs come first, and only where the session asked for them.</summary>
    [Fact]
    public void TheConditionalPairsComeBeforeTheTail()
    {
        IReadOnlyList<ushort> both = CtrlReactions.EnableFeatures(new CtrlFeatures(true, true));

        Assert.Equal(
            [(ushort)CtrlMessage.EnableDualSenseFeatures, 0x11,
             (ushort)CtrlMessage.KeyboardEnable, (ushort)CtrlMessage.KeyboardEnableToggle,
             (ushort)CtrlMessage.MicToggle, (ushort)CtrlMessage.MicToggle,
             (ushort)CtrlMessage.DisplayDevices],
            both);
    }

    /// <summary>A second session id does nothing at all - not even the burst.</summary>
    [Fact]
    public void ASecondSessionIdIsDropped()
    {
        Assert.Empty(CtrlReactions.Answer(
            (ushort)CtrlMessage.SessionId, new CtrlFeatures(), new CtrlSeen(SessionIdReceived: true)));

        Assert.Equal(
            SessionIdVerdict.Dropped,
            CtrlReactions.JudgeSessionId(Valid(), new CtrlSeen(SessionIdReceived: true)));
    }

    /// <summary>And the stream switch is acted on exactly once.</summary>
    [Fact]
    public void TheStreamSwitchIsActedOnOnce()
    {
        Assert.True(CtrlReactions.SwitchIsActedOn(new CtrlSeen()));
        Assert.False(CtrlReactions.SwitchIsActedOn(new CtrlSeen(SwitchReceived: true)));
    }

    /// <summary>Most types are received and answered with nothing.</summary>
    [Theory]
    [InlineData(CtrlMessage.DisplayB)]
    [InlineData(CtrlMessage.Login)]
    [InlineData(CtrlMessage.KeyboardOpen)]
    [InlineData(CtrlMessage.SwitchToStreamConnection)]
    public void MostTypesProduceNothingOnTheWire(CtrlMessage received)
    {
        Assert.Empty(CtrlReactions.Answer((ushort)received, new CtrlFeatures(), new CtrlSeen()));
    }

    /// <summary>A valid session id: the marker byte, then 24 or more alphanumeric characters.</summary>
    private static byte[] Valid()
        => [CtrlReactions.SessionIdMarker, .. Encoding.ASCII.GetBytes(new string('a', 30))];

    /// <summary>The ladder, rung by rung.</summary>
    [Fact]
    public void AValidSessionIdIsAccepted()
    {
        Assert.Equal(SessionIdVerdict.Accepted, CtrlReactions.JudgeSessionId(Valid(), new CtrlSeen()));
    }

    /// <summary>
    /// EVERY FAILURE IS A FALLBACK, NOT AN ERROR.
    ///
    /// An unusable session id does not end the session - a generated one is substituted and the
    /// connect carries on. A port that refused here would refuse consoles the C connects to.
    /// </summary>
    /// <remarks>
    /// 0 is nothing at all; 1 leaves nothing once the marker is dropped; 20 is under the
    /// twenty-four minimum; 80 is at the maximum, which leaves no room for the terminator.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(80)]
    public void AnUnusableLengthFallsBack(int length)
    {
        byte[] payload = length == 0
            ? []
            : [CtrlReactions.SessionIdMarker, .. Encoding.ASCII.GetBytes(new string('a', length - 1))];

        Assert.Equal(SessionIdVerdict.Fallback, CtrlReactions.JudgeSessionId(payload, new CtrlSeen()));
    }

    /// <summary>A character outside a-zA-Z0-9 falls back, wherever it is.</summary>
    [Theory]
    [InlineData('-')]
    [InlineData('_')]
    [InlineData(' ')]
    [InlineData('\0')]
    public void ANonAlphanumericCharacterFallsBack(char bad)
    {
        byte[] payload =
        [
            CtrlReactions.SessionIdMarker,
            .. Encoding.ASCII.GetBytes(new string('a', 20) + bad + new string('b', 9)),
        ];

        Assert.Equal(SessionIdVerdict.Fallback, CtrlReactions.JudgeSessionId(payload, new CtrlSeen()));
    }

    /// <summary>
    /// THE MARKER BYTE IS WARNED ABOUT AND NEVER ENFORCED, which is the rung easiest to port wrong.
    ///
    /// ctrl.c logs "presumably invalid Session Id" and then uses it anyway. A port that refused a
    /// wrong marker would drop ids the C accepts.
    /// </summary>
    [Fact]
    public void AWrongMarkerByteIsStillAccepted()
    {
        byte[] payload = [0x00, .. Encoding.ASCII.GetBytes(new string('a', 30))];

        Assert.Equal(SessionIdVerdict.Accepted, CtrlReactions.JudgeSessionId(payload, new CtrlSeen()));
    }

    /// <summary>And ctrl.c still reacts the way this describes.</summary>
    [Fact]
    public void CtrlStillDeclaresTheseReactions()
    {
        string? path = CtrlReactionsSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            CtrlReactionsSource.ASessionIdStillEnablesFeatures(core),
            "a session id no longer triggers the feature burst from inside the switch");
        Assert.True(
            CtrlReactionsSource.TheMicrophoneIsStillToggledTwice(core),
            "the microphone is no longer toggled twice");
        Assert.True(
            CtrlReactionsSource.TheBurstStillEndsWithDisplayDevices(core),
            "the burst no longer ends with display-devices");
        // Through the body reader, because ctrl.c forward-declares every handler and a search for
        // the name alone lands on a prototype - the trap MessageTapSource documents.
        string? heartbeat = CtrlReactionsSource.HandlerBody(path, "ctrl_message_received_heartbeat_req");
        string? sessionId = CtrlReactionsSource.HandlerBody(path, "ctrl_message_received_session_id");

        Assert.NotNull(heartbeat);
        Assert.NotNull(sessionId);

        Assert.True(
            CtrlReactionsSource.AHeartbeatIsStillAnsweredRegardless(heartbeat),
            "a heartbeat with a payload is no longer answered");
        Assert.True(
            CtrlReactionsSource.AnUnusableSessionIdStillFallsBack(sessionId),
            "an unusable session id no longer falls back on all four rungs");
    }
}
