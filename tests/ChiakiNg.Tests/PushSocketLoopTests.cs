using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP214: the push socket's frame loop, asserted without a socket.
///
/// The two tests worth reading first are the pair on the keepalive: at ONE instant the loop either
/// pings or gives up, and which one it does depends on nothing but whether a pong is outstanding.
/// That is not a deadline sitting near a cadence - it is the same subtraction asked twice.
/// </summary>
public class PushSocketLoopTests
{
    /// <summary>An instant the loop's clock could plausibly report: an hour and a half of uptime.</summary>
    private const long SomeUptimeUs = 90 * 60 * PushSocketLoop.MicrosecondsPerSecond;

    /// <summary>Both halves of the keepalive fire at the same instant, and only one can win.</summary>
    [Fact]
    public void APongIsOverdueAtTheSameInstantAPingWouldGoOut()
    {
        long sent = SomeUptimeUs;
        long due = sent + PushSocketLoop.PingIntervalUs + 1;

        Assert.Equal(KeepaliveStep.PongOverdue, PushSocketLoop.Next(due, sent, expectingPong: true));
        Assert.Equal(KeepaliveStep.SendPing, PushSocketLoop.Next(due, sent, expectingPong: false));
    }

    /// <summary>
    /// And there is nothing in between. A pong outstanding is dropped the moment the interval is
    /// past, not an interval later - because the two tests share their one number.
    /// </summary>
    [Fact]
    public void ThereIsNoGraceBeyondTheCadence()
    {
        long sent = SomeUptimeUs;

        for (long ahead = 1; ahead <= 4 * PushSocketLoop.PingIntervalUs; ahead *= 2)
        {
            long at = sent + PushSocketLoop.PingIntervalUs + ahead;

            Assert.Equal(KeepaliveStep.PongOverdue, PushSocketLoop.Next(at, sent, expectingPong: true));
        }
    }

    /// <summary>The comparison is strict, so the instant the interval EQUALS it is still a read.</summary>
    [Fact]
    public void TheIntervalIsPastRatherThanReached()
    {
        long sent = SomeUptimeUs;
        long exactly = sent + PushSocketLoop.PingIntervalUs;

        Assert.Equal(KeepaliveStep.Read, PushSocketLoop.Next(exactly, sent, expectingPong: false));
        Assert.Equal(KeepaliveStep.Read, PushSocketLoop.Next(exactly, sent, expectingPong: true));
    }

    /// <summary>Before the interval, with no pong outstanding, the loop just reads.</summary>
    [Fact]
    public void InsideTheIntervalTheLoopReads()
        => Assert.Equal(
            KeepaliveStep.Read,
            PushSocketLoop.Next(SomeUptimeUs + 1, SomeUptimeUs, expectingPong: true));

    /// <summary>
    /// The first turn pings - and the reason is the CLOCK, not the arithmetic. last_ping_sent
    /// starts at zero and the clock is monotonic, so on any machine that has been up for five
    /// seconds the interval has already elapsed. A clock that really did start at zero would read.
    /// </summary>
    [Fact]
    public void TheFirstTurnPingsBecauseTheClockIsNotZero()
    {
        Assert.Equal(KeepaliveStep.SendPing, PushSocketLoop.Next(SomeUptimeUs, 0, expectingPong: false));
        Assert.Equal(KeepaliveStep.Read, PushSocketLoop.Next(0, 0, expectingPong: false));
    }

    /// <summary>Each frame kind on its own, and what it asks for.</summary>
    [Theory]
    [InlineData(WebSocketFrameKind.Pong, FrameAction.StopExpectingPong)]
    [InlineData(WebSocketFrameKind.Ping, FrameAction.SendPong)]
    [InlineData(WebSocketFrameKind.Close, FrameAction.Close)]
    [InlineData(WebSocketFrameKind.Text, FrameAction.Deliver)]
    [InlineData(WebSocketFrameKind.Binary, FrameAction.Deliver)]
    [InlineData(WebSocketFrameKind.None, FrameAction.None)]
    public void EachFrameKindAsksForOneThing(WebSocketFrameKind flags, FrameAction expected)
        => Assert.Equal(expected, PushSocketLoop.ActionsFor(flags));

    /// <summary>Text and binary are one answer between them, which is what the core asks.</summary>
    [Fact]
    public void TextAndBinaryAreTheSameAnswer()
        => Assert.Equal(
            PushSocketLoop.ActionsFor(WebSocketFrameKind.Text),
            PushSocketLoop.ActionsFor(WebSocketFrameKind.Binary));

    /// <summary>
    /// The tests are independent, so the answers add up. A frame flagged both PING and CLOSE is
    /// answered AND ends the loop - a port that chose one kind per frame would drop one of them.
    /// </summary>
    [Fact]
    public void AFrameCarryingTwoKindsAsksForBoth()
    {
        Assert.Equal(
            FrameAction.SendPong | FrameAction.Close,
            PushSocketLoop.ActionsFor(WebSocketFrameKind.Ping | WebSocketFrameKind.Close));

        Assert.Equal(
            FrameAction.StopExpectingPong | FrameAction.Deliver,
            PushSocketLoop.ActionsFor(WebSocketFrameKind.Pong | WebSocketFrameKind.Text));
    }

    /// <summary>A pong answers with the ping's own bytes rather than with an empty frame.</summary>
    [Fact]
    public void ThePongCarriesThePingsBytes()
    {
        byte[] ping = [0x9, 0x1, 0x2, 0x3];

        Assert.Equal(ping, PushSocketLoop.PongPayloadFor(ping).ToArray());
    }

    /// <summary>The numbers, as the core spells them.</summary>
    [Fact]
    public void TheNumbersAreTheCores()
    {
        Assert.Equal(5, PushSocketLoop.PingIntervalSeconds);
        Assert.Equal(5_000_000, PushSocketLoop.PingIntervalUs);
        Assert.Equal(5_000, PushSocketLoop.SelectTimeoutMs);
        Assert.Equal(65_536, PushSocketLoop.MaxFrameSize);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheFrameLoopIsStillTheCores()
    {
        string? file = PushSocketLoopSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PushSocketLoopSource.ThePongDeadlineIsStillTheCadence(core),
            "the same interval, and asked first");
        Assert.True(
            PushSocketLoopSource.TheConstantStillGovernsTheWaitAlone(core),
            "the constant is spent on the wait");
        Assert.True(PushSocketLoopSource.TheIntervalIsStillFiveSeconds(core), "and it is still five");
        Assert.True(
            PushSocketLoopSource.TheFrameTestsAreStillIndependent(core),
            "four ifs, not a switch");
        Assert.True(
            PushSocketLoopSource.ThePongStillCarriesThePingsBytes(core),
            "rlen back, zero out");
        Assert.True(PushSocketLoopSource.TheFrameSizeIsStillThis(core), "and 64 KiB bounds a frame");
    }
}
