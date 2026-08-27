using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP450, PP27: takion's handshake - the four messages that have to cross before the transport
/// exists.
///
/// PP449 covered the receive thread's timer, which has nothing to do until this has finished. The
/// assertions worth having here are the asymmetries: which failure retries and which aborts, which
/// of the two gates reports a bad init ack, and which side of the stream comparison is which. All
/// three look arbitrary and all three are load-bearing on a lossy link or against a console that
/// answers with numbers this one has never seen.
/// </summary>
public class TakionHandshakeTests
{
    private static string? Takion()
    {
        string? path = TakionHandshake.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    private static TakionInitAck GoodAck(uint tag = 0x4823)
        => new(tag, TakionHandshake.ARwnd, 0x64, 0x64, InitialSeqNum: 0xdeadbeef);

    /// <summary>A well-formed init ack passes both gates.</summary>
    [Fact]
    public void AWellFormedInitAckIsAccepted()
    {
        Assert.Equal(TakionInitAckVerdict.Accepted, TakionHandshake.Check(GoodAck()));
    }

    /// <summary>
    /// THE GATE ORDER: an ack that is wrong twice is refused for its TAG.
    ///
    /// The C logs the two separately and nothing else distinguishes them, so a port that checked the
    /// streams first would put a different sentence in the log for the same packet - which is the only
    /// evidence anybody has when a console refuses to connect.
    /// </summary>
    [Fact]
    public void AZeroTagIsReportedBeforeImpossibleStreamCounts()
    {
        var wrongTwice = new TakionInitAck(Tag: 0, ARwnd: 0, OutboundStreams: 0, InboundStreams: 0, 0);

        Assert.Equal(TakionInitAckVerdict.ZeroTag, TakionHandshake.Check(wrongTwice));
    }

    /// <summary>And a good tag with bad counts takes the other verdict.</summary>
    [Fact]
    public void GoodTagAndBadCountsIsRefusedForTheCounts()
    {
        TakionInitAck ack = GoodAck() with { OutboundStreams = 0x65 };

        Assert.Equal(TakionInitAckVerdict.StreamCountsRefused, TakionHandshake.Check(ack));
    }

    /// <summary>
    /// THE STREAM CHECK IS CROSSED: the console's outbound is bounded ABOVE by our inbound, and its
    /// inbound bounded BELOW by our outbound.
    /// </summary>
    [Theory]
    [InlineData((ushort)0x64, (ushort)0x64, true)]  // the real answer
    [InlineData((ushort)0x63, (ushort)0x64, true)]  // it sends on fewer than we listen to: fine
    [InlineData((ushort)0x64, (ushort)0x65, true)]  // it listens to more than we send on: fine
    [InlineData((ushort)0x65, (ushort)0x64, false)] // it would send on a stream we do not read
    [InlineData((ushort)0x64, (ushort)0x63, false)] // it would not listen to one we write
    [InlineData((ushort)0, (ushort)0x64, false)]
    [InlineData((ushort)0x64, (ushort)0, false)]
    public void TheStreamCountsAreComparedCrosswise(ushort outbound, ushort inbound, bool expected)
    {
        Assert.Equal(expected, TakionHandshake.StreamCountsAgree(outbound, inbound));
    }

    /// <summary>
    /// And the inversion is caught, which is the only reason the test above is worth having.
    ///
    /// Both constants are 0x64, so swapping the two comparisons passes every handshake a real console
    /// produces. This is the one pair of numbers that tells the two rules apart: the real rule refuses
    /// it, and the swapped one accepts it.
    /// </summary>
    [Fact]
    public void SwappingTheTwoComparisonsWouldAcceptWhatTheCRefuses()
    {
        const ushort outbound = 0x65;
        const ushort inbound = 0x63;

        Assert.False(TakionHandshake.StreamCountsAgree(outbound, inbound));

        // What an inverted port would compute, spelled out rather than described.
        bool inverted = inbound <= TakionHandshake.InboundStreams
            && outbound >= TakionHandshake.OutboundStreams;
        Assert.True(inverted);
    }

    /// <summary>An ack on the first attempt costs one attempt.</summary>
    [Fact]
    public void AnAckOnTheFirstAttemptEndsIt()
    {
        TakionExchange result = TakionHandshake.Exchange(
            _ => (ChiakiError.Success, ChiakiError.Success));

        Assert.Equal(1, result.Attempts);
        Assert.Equal(ChiakiError.Success, result.Error);
    }

    /// <summary>
    /// A SEND FAILURE ABORTS AT ONCE. One attempt, and the SEND's error - not a timeout, and not
    /// three packets at a socket that has already said no.
    /// </summary>
    [Fact]
    public void ASendFailureAbortsOnTheFirstAttempt()
    {
        TakionExchange result = TakionHandshake.Exchange(
            _ => (ChiakiError.Network, ChiakiError.Success));

        Assert.Equal(1, result.Attempts);
        Assert.Equal(ChiakiError.Network, result.Error);
    }

    /// <summary>A lost ack retries, and the third attempt still counts as a connect.</summary>
    [Fact]
    public void ALostAckRetriesUpToThreeTimes()
    {
        TakionExchange result = TakionHandshake.Exchange(
            tries => (ChiakiError.Success, tries == 2 ? ChiakiError.Success : ChiakiError.Timeout));

        Assert.Equal(3, result.Attempts);
        Assert.Equal(ChiakiError.Success, result.Error);
    }

    /// <summary>
    /// And after three lost acks the caller sees the LAST receive's error, whatever the earlier ones
    /// said - so a run ending in a timeout reports a timeout rather than an error of its own.
    /// </summary>
    [Fact]
    public void ThreeLostAcksReportTheLastReceivesError()
    {
        ChiakiError[] receives = [ChiakiError.Network, ChiakiError.InvalidResponse, ChiakiError.Timeout];

        TakionExchange result = TakionHandshake.Exchange(
            tries => (ChiakiError.Success, receives[tries]));

        Assert.Equal(3, result.Attempts);
        Assert.Equal(ChiakiError.Timeout, result.Error);
    }

    /// <summary>A send that fails on a LATER attempt aborts there, mid-retry.</summary>
    [Fact]
    public void ASendFailureOnARetryAbortsThere()
    {
        TakionExchange result = TakionHandshake.Exchange(
            tries => tries == 0
                ? (ChiakiError.Success, ChiakiError.Timeout)
                : (ChiakiError.HostDown, ChiakiError.Success));

        Assert.Equal(2, result.Attempts);
        Assert.Equal(ChiakiError.HostDown, result.Error);
    }

    /// <summary>
    /// THE TAG IS THE INITIAL SEQUENCE NUMBER, in both directions - and the ack's own field is
    /// ignored.
    ///
    /// The two are given different values here precisely so the assertion can tell them apart: a port
    /// reading the wire field would agree with the C only while the console kept setting them equal.
    /// </summary>
    [Fact]
    public void TheRemoteInitialSeqNumIsTheTagAndNotTheWireField()
    {
        TakionInitAck ack = GoodAck(tag: 0x1234) with { InitialSeqNum = 0xffff0000 };

        Assert.Equal(0x1234u, TakionHandshake.RemoteInitialSeqNum(ack));
        Assert.NotEqual(ack.InitialSeqNum, TakionHandshake.RemoteInitialSeqNum(ack));
    }

    /// <summary>The local half of the same convention, and the INIT built from it.</summary>
    [Fact]
    public void TheInitAdvertisesTheLocalTagAsBothItsTagAndItsSeqNum()
    {
        const uint tagLocal = 0x4823;

        TakionInitAck init = TakionHandshake.Init(tagLocal);

        Assert.Equal(tagLocal, init.Tag);
        Assert.Equal(tagLocal, init.InitialSeqNum);
        Assert.Equal(TakionHandshake.ARwnd, init.ARwnd);
        Assert.Equal(TakionHandshake.OutboundStreams, init.OutboundStreams);
        Assert.Equal(TakionHandshake.InboundStreams, init.InboundStreams);
    }

    /// <summary>
    /// The header tag runs the other way: outbound carries the PEER's, which is 0 for the INIT, and an
    /// inbound message is refused unless it carries OURS.
    /// </summary>
    [Fact]
    public void TheHeaderTagIsThePeersOutboundAndOursInbound()
    {
        const uint tagLocal = 0x4823;

        // Before the init ack, tag_remote is 0 - so the INIT asks with a header addressed to nobody.
        Assert.Equal(0u, TakionHandshake.OutboundHeaderTag(0));
        Assert.Equal(0x9999u, TakionHandshake.OutboundHeaderTag(0x9999));

        Assert.True(TakionHandshake.InboundHeaderTagAccepted(tagLocal, tagLocal));
        Assert.False(TakionHandshake.InboundHeaderTagAccepted(0x9999, tagLocal));
    }

    /// <summary>
    /// DEFECT: the cookie ack decides its second-init-ack branch on byte 0xd before checking that the
    /// datagram is long enough to have one.
    ///
    /// Fourteen bytes is the boundary. Below it the branch reads uninitialised stack - inside the
    /// array, which is why no sanitiser has ever pointed at it - and a value that happens to equal the
    /// INIT_ACK chunk type consumes a datagram nobody asked for.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(13, false)]
    [InlineData(14, true)]
    [InlineData(17, true)]
    public void TheSecondInitAckTestNeedsFourteenBytesToBeReadingData(int receivedSize, bool delivered)
    {
        Assert.Equal(delivered, TakionHandshake.SecondInitAckTestReadsDeliveredBytes(receivedSize));
    }

    /// <summary>
    /// DEFECT: and the second receive inherits the first datagram's length as its capacity, so the
    /// short datagram that made the branch fire also truncates what the branch goes to fetch.
    ///
    /// A real second init ack is 65 bytes, so on the path this was written for the capacity is already
    /// the full 17 and nothing is narrowed - which is why it has never been noticed.
    /// </summary>
    [Fact]
    public void TheSecondReceiveIsNarrowedByAShortFirstDatagram()
    {
        Assert.Equal(4, TakionHandshake.SecondReceiveCapacity(4));

        // Not narrowed where the first datagram filled the buffer.
        Assert.Equal(
            TakionHandshake.CookieAckDatagramSize,
            TakionHandshake.SecondReceiveCapacity(TakionHandshake.CookieAckDatagramSize));
    }

    /// <summary>The two exact datagram lengths, as the sizeof expressions compute them.</summary>
    [Fact]
    public void TheTwoExpectedDatagramLengths()
    {
        Assert.Equal(65, TakionHandshake.InitAckDatagramSize);
        Assert.Equal(17, TakionHandshake.CookieAckDatagramSize);
    }

    /// <summary>Every constant is the C's, read from its define rather than trusted here.</summary>
    [Fact]
    public void TheConstantsAreStillTheCs()
    {
        if (Takion() is not { } source)
            return;

        Assert.Equal(
            (long?)TakionHandshake.MaxConnectResendTries,
            TakionHandshake.DefineIn(source, "MAX_CONNECT_RESEND_TRIES"));
        Assert.Equal(
            (long?)TakionHandshake.ExpectTimeoutMs,
            TakionHandshake.DefineIn(source, "TAKION_EXPECT_TIMEOUT_MS"));
        Assert.Equal(
            (long?)TakionHandshake.ARwnd, TakionHandshake.DefineIn(source, "TAKION_A_RWND"));
        Assert.Equal(
            (long?)TakionHandshake.OutboundStreams,
            TakionHandshake.DefineIn(source, "TAKION_OUTBOUND_STREAMS"));
        Assert.Equal(
            (long?)TakionHandshake.InboundStreams,
            TakionHandshake.DefineIn(source, "TAKION_INBOUND_STREAMS"));
        Assert.Equal(
            (long?)TakionHandshake.CookieSize, TakionHandshake.DefineIn(source, "TAKION_COOKIE_SIZE"));
        Assert.Equal(
            (long?)TakionHandshake.MessageHeaderSize,
            TakionHandshake.DefineIn(source, "TAKION_MESSAGE_HEADER_SIZE"));
    }

    /// <summary>
    /// And the retry count is above zero, which is what makes the C's uninitialised `err` unreachable
    /// rather than merely unlikely: at 0 neither loop runs and the test after it reads a garbage value.
    /// </summary>
    [Fact]
    public void TheRetryCountIsAboveZeroSoTheUninitialisedErrIsUnreachable()
    {
        if (Takion() is not { } source)
            return;

        long? tries = TakionHandshake.DefineIn(source, "MAX_CONNECT_RESEND_TRIES");

        Assert.NotNull(tries);
        Assert.True(
            tries > 0,
            "MAX_CONNECT_RESEND_TRIES is 0, so takion_handshake tests an uninitialised err");
    }

    /// <summary>The retry rule is still written the way this models it.</summary>
    [Fact]
    public void TheRetryRuleIsStillInTheC()
    {
        if (Takion() is not { } source || TakionHandshake.HandshakeBody(source) is not { } body)
            return;

        Assert.True(
            TakionHandshake.ASendFailureStillAborts(body),
            "a send failure no longer returns at once, so a dead socket now costs three packets and "
                + "this model is behind the C");
        Assert.True(TakionHandshake.AReceivedAckStillEndsTheLoop(body));
        Assert.True(TakionHandshake.BothLoopsAreStillBounded(body));
    }

    /// <summary>The two gates, their order, and the crossed comparison.</summary>
    [Fact]
    public void TheInitAckGatesAreStillInTheC()
    {
        if (Takion() is not { } source || TakionHandshake.HandshakeBody(source) is not { } body)
            return;

        Assert.True(TakionHandshake.TheZeroTagGateIsStillFirst(body));
        Assert.True(TakionHandshake.TheStreamCheckIsStillCrossed(body));
        Assert.True(
            TakionHandshake.TheRemoteSeqNumIsStillTheTag(body),
            "the remote initial sequence number is no longer the tag, or the wire field it was chosen "
                + "over is no longer commented out beside it");
    }

    /// <summary>Both cookie ack defects are still there, and the init ack still does it right.</summary>
    [Fact]
    public void BothCookieAckDefectsAreStillInTheC()
    {
        if (Takion() is not { } source)
            return;

        if (TakionHandshake.CookieAckBody(source) is not { } cookie
            || TakionHandshake.InitAckBody(source) is not { } init)
        {
            return;
        }

        Assert.True(TakionHandshake.TheSecondInitAckTestPrecedesTheSizeCheck(cookie));
        Assert.True(TakionHandshake.TheSecondReceiveInheritsTheFirstLength(cookie));

        // The same function twenty lines up checks its length before reading a byte, which is what
        // makes the cookie ack a slip rather than a house style.
        Assert.True(TakionHandshake.TheInitAckChecksItsLengthFirst(init));
    }

    /// <summary>
    /// PP450's define reader: both bases, and a name matched whole rather than as a prefix.
    /// </summary>
    [Fact]
    public void TheDefineReaderHandlesBothBasesAndWholeNames()
    {
        const string source = """
            #define A_COUNT 3
            #define A_SIZE 0x20
            #define A_SIZE_MAX 0x40
            #  define SPACED 7
            #define NOT_A_NUMBER SOMETHING_ELSE
            """;

        Assert.Equal(3L, CDefine.Value(source, "A_COUNT"));
        Assert.Equal(0x20L, CDefine.Value(source, "A_SIZE"));
        Assert.Equal(0x40L, CDefine.Value(source, "A_SIZE_MAX"));
        Assert.Equal(7L, CDefine.Value(source, "SPACED"));
        Assert.Null(CDefine.Value(source, "NOT_A_NUMBER"));
        Assert.Null(CDefine.Value(source, "A_"));
        Assert.Null(CDefine.Value("", "A_COUNT"));
    }

    /// <summary>PP272: and every reader says no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(TakionHandshake.HandshakeBody(""));
        Assert.Null(TakionHandshake.CookieAckBody(""));
        Assert.Null(TakionHandshake.InitAckBody(""));
        Assert.Null(TakionHandshake.DefineIn("", "MAX_CONNECT_RESEND_TRIES"));
        Assert.False(TakionHandshake.ASendFailureStillAborts(""));
        Assert.False(TakionHandshake.AReceivedAckStillEndsTheLoop(""));
        Assert.False(TakionHandshake.BothLoopsAreStillBounded(""));
        Assert.False(TakionHandshake.TheRemoteSeqNumIsStillTheTag(""));
        Assert.False(TakionHandshake.TheZeroTagGateIsStillFirst(""));
        Assert.False(TakionHandshake.TheStreamCheckIsStillCrossed(""));
        Assert.False(TakionHandshake.TheSecondInitAckTestPrecedesTheSizeCheck(""));
        Assert.False(TakionHandshake.TheSecondReceiveInheritsTheFirstLength(""));
        Assert.False(TakionHandshake.TheInitAckChecksItsLengthFirst(""));
    }
}
