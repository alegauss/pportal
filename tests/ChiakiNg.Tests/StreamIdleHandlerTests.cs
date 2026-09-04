using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP688, under PP295: the handler everything after setup arrives at, and its one rule.
///
/// The switch is four arms and a default that does nothing; the rule is what a connection quality
/// report does to the round trip this port reports. Both are held against streamconnection.c,
/// because no shim wraps this file and the C's text is the only oracle a checkout has for it.
/// </summary>
public class StreamIdleHandlerTests(ITestOutputHelper output)
{
    /// <summary>The handler's body, or null outside a checkout.</summary>
    private static string? Body()
    {
        if (StreamIdleHandlerSource.Locate() is not { } path)
            return null;

        return StreamIdleHandlerSource.HandlerBody(File.ReadAllText(path));
    }

    /// <summary>Each named type reaches its own arm, and the messages this port builds are routed.</summary>
    [Fact]
    public void EachNamedTypeReachesItsOwnArm()
    {
        Assert.Equal(
            IdleAction.Disconnect, StreamIdleHandler.Route(StreamMessages.DisconnectType));
        Assert.Equal(
            IdleAction.ReadQuality, StreamIdleHandler.Route(StreamIdleHandler.ConnectionQualityType));
        Assert.Equal(
            IdleAction.LogCorruptFrame, StreamIdleHandler.Route(StreamMessages.CorruptFrameType));
        Assert.Equal(
            IdleAction.LogStreamInfoAck,
            StreamIdleHandler.Route(StreamExchangeParticipant.StreamInfoAckType));
    }

    /// <summary>
    /// And everything else is ignored, including messages this side sends - which is right, because
    /// the arms are about what a console says and not about what a type is.
    /// </summary>
    [Theory]
    [InlineData(StreamMessages.HeartbeatType)]
    [InlineData(StreamMessages.IdrRequestType)]
    [InlineData(StreamExchangeParticipant.StreamInfo)]
    [InlineData(StreamExchangeParticipant.ControllerConnectionType)]
    [InlineData((ushort)0)]
    [InlineData((ushort)999)]
    public void EverythingElseIsIgnored(ushort payloadType)
        => Assert.Equal(IdleAction.Ignore, StreamIdleHandler.Route(payloadType));

    /// <summary>
    /// A REPORTED ROUND TRIP IS MILLISECONDS, so the number this port keeps is a thousand times it.
    ///
    /// Measured rather than assumed, and the measurement is in the C: over a session the console
    /// reported 36 to 295 while ICMP to the same console measured 3 to 31. Read as microseconds the
    /// console would be forty times faster than ICMP on the same link.
    /// </summary>
    [Theory]
    [InlineData(36.0, 36000UL)]
    [InlineData(295.0, 295000UL)]
    [InlineData(1.5, 1500UL)]
    [InlineData(0.5, 500UL)]
    public void APositiveRoundTripIsTakenAsMilliseconds(double rtt, ulong microseconds)
    {
        QualityReading reading = StreamIdleHandler.ReadQuality(rtt, lastReported: 7);

        Assert.True(reading.Accepted);
        Assert.Equal(microseconds, reading.ReportedRttMicroseconds);
    }

    /// <summary>
    /// A ZERO IS NOT A ROUND TRIP OF NO TIME: it is the console saying it has nothing yet, so the
    /// last real reading survives it.
    ///
    /// The half a port drops, because a guard against zero reads as defensiveness until you know
    /// what the zero means. The other values here are what a double can carry and a duration cannot.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-0.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NothingUsableKeepsTheLastReading(double rtt)
    {
        QualityReading reading = StreamIdleHandler.ReadQuality(rtt, lastReported: 41000);

        Assert.False(reading.Accepted);
        Assert.Equal(41000UL, reading.ReportedRttMicroseconds);
    }

    /// <summary>
    /// And with nothing reported before, an unusable report still leaves nothing rather than a zero
    /// that reads as a measurement.
    /// </summary>
    [Fact]
    public void WithNoPreviousReadingThereIsStillNone()
    {
        QualityReading reading = StreamIdleHandler.ReadQuality(0.0, lastReported: 0);

        Assert.False(reading.Accepted);
        Assert.Equal(0UL, reading.ReportedRttMicroseconds);
    }

    /// <summary>
    /// THE C STILL NAMES THESE FOUR, and no more - so a fifth arm is a failure here rather than a
    /// message this port silently stopped handling.
    /// </summary>
    [Fact]
    public void TheCStillNamesTheseFourCases()
    {
        if (Body() is not { } body)
            return;

        IReadOnlyList<string> cases = StreamIdleHandlerSource.CasesIn(body);
        output.WriteLine(string.Join(", ", cases));

        Assert.Equal(
            ["DISCONNECT", "CONNECTIONQUALITY", "CORRUPTFRAME", "STREAMINFOACK"],
            cases);

        Assert.Equal(cases.Count, StreamIdleHandler.Handled.Count);
    }

    /// <summary>And its default still does nothing at all, which is what makes the rest dropped.</summary>
    [Fact]
    public void TheDefaultStillDoesNothing()
    {
        if (Body() is not { } body)
            return;

        Assert.True(
            StreamIdleHandlerSource.TheDefaultStillDoesNothing(body),
            "the idle handler's default arm now does something, so a message this port ignores is "
                + "one the C acts on");
    }

    /// <summary>
    /// The round trip's guard still has both halves, and the conversion is still a thousand.
    ///
    /// One without the other is a different rule: without the finiteness test an infinity becomes a
    /// reading, and without the comparison a zero erases the last real one.
    /// </summary>
    [Fact]
    public void TheRoundTripRuleIsStillTheCs()
    {
        if (Body() is not { } body)
            return;

        Assert.True(
            StreamIdleHandlerSource.TheRoundTripIsStillGuardedBothWays(body),
            "the reported round trip is no longer guarded on both finiteness and sign");
        Assert.True(
            StreamIdleHandlerSource.TheConversionIsStillAThousand(body),
            "the round trip is no longer multiplied by a thousand, so the field is no longer being "
                + "read as milliseconds and this port's number is out by that factor");
    }

    /// <summary>
    /// The bitrate reading still resets the statistics it read, which is what makes it per message.
    ///
    /// Without the reset the same bytes are counted into every later report and the number climbs
    /// for the length of the session - a reading that looks like a measurement and is a total.
    /// </summary>
    [Fact]
    public void TheBitrateReadingStillResets()
    {
        if (Body() is not { } body)
            return;

        Assert.True(StreamIdleHandlerSource.TheBitrateReadResets(body));
    }

    /// <summary>
    /// The four this port routes are the four the C names, joined by number rather than by position.
    /// </summary>
    [Fact]
    public void TheRoutedTypesAreTheNumbersTheProtoAssigns()
    {
        Assert.Equal(
            (ushort)Tkproto.TakionMessage.Types.PayloadType.Connectionquality,
            StreamIdleHandler.ConnectionQualityType);

        Assert.All(
            StreamIdleHandler.Handled,
            type => Assert.NotEqual(IdleAction.Ignore, StreamIdleHandler.Route(type)));
    }

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(StreamIdleHandlerSource.HandlerBody(""));
        Assert.Empty(StreamIdleHandlerSource.CasesIn(""));
        Assert.False(StreamIdleHandlerSource.TheRoundTripIsStillGuardedBothWays(""));
        Assert.False(StreamIdleHandlerSource.TheConversionIsStillAThousand(""));
        Assert.False(StreamIdleHandlerSource.TheBitrateReadResets(""));
        Assert.False(StreamIdleHandlerSource.TheDefaultStillDoesNothing(""));
    }
}
