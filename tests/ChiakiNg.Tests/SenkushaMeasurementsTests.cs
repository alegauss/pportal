using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP789, under PP784: the three measurements, and the arithmetic a port can get wrong silently.
///
/// Senkusha measures a link and stops, and PP777 found what the numbers are for: the launch spec
/// carries mtu_in and the round trip, and a console told a link nobody has answers a BIG it will
/// not act on. Nothing on the wire looks different when the arithmetic is wrong.
///
/// THREE THINGS HERE ARE EASY TO TIDY AND WRONG TO. The average divides by the pings that ANSWERED
/// rather than by the ten sent; the two searches start in different places; and the outbound test's
/// closing message reports the ceiling while its answer is the floor.
/// </summary>
public class SenkushaMeasurementsTests(ITestOutputHelper output)
{
    private static string? Source()
        => SenkushaMeasurementsSource.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE AVERAGE IS OVER WHAT CAME BACK, which is the divisor and nothing else.
    ///
    /// A link that loses half its pings reports the round trip of the half that survived. Dividing
    /// by ten would report it as twice as slow, and the launch spec spends that number.
    /// </summary>
    [Fact]
    public void TheRoundTripIsAveragedOverTheAnsweredOnly()
    {
        SenkushaRttReading half = SenkushaMeasurements.Average([1000, 2000, 3000, 4000, 5000]);

        Assert.Equal(ChiakiError.Success, half.Outcome);
        Assert.Equal(5, half.Answered);
        Assert.Equal(3000ul, half.AverageMicroseconds);

        // Ten sent and five back is still three milliseconds, not one and a half.
        Assert.Equal(SenkushaMeasurements.PingCount, 10);

        // And a test that got nothing back is UNKNOWN, which is not what a stop answers.
        SenkushaRttReading none = SenkushaMeasurements.Average([]);

        Assert.Equal(ChiakiError.Unknown, none.Outcome);
        Assert.Equal(0, none.Answered);

        if (Source() is not { } source)
            return;

        string body = SenkushaMeasurementsSource.TestBody(source, "rtt_test")
            ?? throw new InvalidOperationException("the rtt test is gone");

        Assert.True(SenkushaMeasurementsSource.TheAverageStillDividesByTheAnswered(body));
        Assert.True(SenkushaMeasurementsSource.ALostPongStillContinues(body));
    }

    /// <summary>
    /// THE MTU TIMEOUT IS DERIVED AND CLAMPED, which is why it is not one of PP788's constants.
    ///
    /// Five round trips in milliseconds, held between 5 and 500. A fast link probes fast and a slow
    /// one gets room, and both are bounded by numbers appearing nowhere else in the file.
    /// </summary>
    [Theory]
    [InlineData(0ul, 5ul)]
    [InlineData(500ul, 5ul)]
    [InlineData(2000ul, 10ul)]
    [InlineData(20000ul, 100ul)]
    [InlineData(100000ul, 500ul)]
    [InlineData(5000000ul, 500ul)]
    public void TheMtuTimeoutIsFiveRoundTripsClamped(ulong rttUs, ulong expected)
    {
        Assert.Equal(expected, SenkushaMeasurements.MtuTimeoutMs(rttUs));

        // And never the five-second constant the other states use.
        Assert.True(SenkushaMeasurements.MtuTimeoutMs(rttUs) < (ulong)SenkushaStates.ExpectTimeoutMs);
    }

    /// <summary>The derivation is the C's, read from the run that performs it once.</summary>
    [Fact]
    public void TheDerivationIsTheRunsOwn()
    {
        if (Source() is not { } source)
            return;

        string run = CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_senkusha_run(")
            ?? throw new InvalidOperationException("the run is gone");

        Assert.True(SenkushaMeasurementsSource.TheTimeoutIsStillFiveRoundTripsClamped(run));
    }

    /// <summary>
    /// A LINK THAT CARRIES THE CEILING IS MEASURED IN ONE PROBE, which is why inbound starts there.
    ///
    /// The common case, and the reason the asymmetry is worth keeping: a search that started at the
    /// midpoint would spend ten probes agreeing with the first one.
    /// </summary>
    [Fact]
    public void AFullSizedLinkSettlesInOneStep()
    {
        IReadOnlyList<SenkushaMtuStep> steps = SenkushaMeasurements.InboundSearch(carries: 1454);

        output.WriteLine(string.Join(", ", steps.Select(one => $"{one.Probed}{(one.Carried ? "+" : "-")}")));

        Assert.Single(steps);
        Assert.Equal(1454u, steps[0].Probed);
        Assert.True(steps[0].Carried);
        Assert.Equal(1454u, SenkushaMeasurements.Settled(steps));
    }

    /// <summary>
    /// AND A NARROWER LINK IS BISECTED DOWN TO IT, answering with the floor and never the probe.
    ///
    /// The window closes when the gap is one, so the answer is the largest size that carried - and
    /// on a search whose last step FAILED, the probe and the answer are different numbers.
    /// </summary>
    [Fact]
    public void ANarrowLinkIsBisectedAndAnswersWithTheFloor()
    {
        IReadOnlyList<SenkushaMtuStep> steps = SenkushaMeasurements.InboundSearch(carries: 1000);

        output.WriteLine(string.Join(", ", steps.Select(one => $"{one.Probed}{(one.Carried ? "+" : "-")}")));

        uint settled = SenkushaMeasurements.Settled(steps);

        // It finds the boundary exactly: the largest size that carried.
        Assert.Equal(1000u, settled);

        // And the window really closed rather than the loop being cut short.
        Assert.False(SenkushaMeasurements.Searching(steps[^1].Min, steps[^1].Max));

        // Every probe that carried is under the link's size and every one that did not is over,
        // which is what makes the bisection a measurement rather than a walk.
        Assert.All(steps, one => Assert.Equal(one.Probed <= 1000, one.Carried));

        // THE ANSWER IS THE FLOOR AND NOT THE PROBE, which the steps show wherever one failed: a
        // failing step leaves the floor where it was and pulls the ceiling down to what it tried.
        SenkushaMtuStep refused = steps.First(one => !one.Carried);

        Assert.Equal(refused.Probed, refused.Max);
        Assert.NotEqual(refused.Probed, refused.Min);
    }

    /// <summary>
    /// THE TWO SEARCHES START APART, which is the whole reason the outbound one takes mtu_in.
    /// </summary>
    [Fact]
    public void TheOutboundSearchStartsWhereTheInboundOneFinished()
    {
        Assert.Equal(1454u, SenkushaMeasurements.FirstInboundProbe(SenkushaMeasurements.MtuMax));
        Assert.Equal(1200u, SenkushaMeasurements.FirstOutboundProbe(1200));

        if (Source() is not { } source)
            return;

        string inBody = SenkushaMeasurementsSource.TestBody(source, "mtu_in_test")!;
        string outBody = SenkushaMeasurementsSource.TestBody(source, "mtu_out_test")!;

        Assert.True(SenkushaMeasurementsSource.TheSearchesStillStartApart(inBody, outBody));
        Assert.True(SenkushaMeasurementsSource.BothStillAnswerWithTheFloor(inBody, outBody));
    }

    /// <summary>
    /// AND THE CLOSING COMMAND REPORTS THE CEILING while the answer is the floor.
    ///
    /// It reads like a slip and it is what the C sends, so it is reproduced. A console told the
    /// floor instead would be told a number this port invented - which is the one thing a port of a
    /// measurement must not do.
    /// </summary>
    [Fact]
    public void TheClosingCommandCarriesTheCeilingNotTheAnswer()
    {
        if (Source() is not { } source)
            return;

        string outBody = SenkushaMeasurementsSource.TestBody(source, "mtu_out_test")!;

        Assert.True(SenkushaMeasurementsSource.TheClosingCommandStillReportsTheCeiling(outBody));

        // And a send that failed retries rather than returning, which the inbound test does not do.
        Assert.True(SenkushaMeasurementsSource.AFailedSendStillRetriesOutbound(outBody));
    }

    /// <summary>
    /// The outbound test refuses four shapes before it sends anything, and its floor is not arbitrary.
    ///
    /// Eight bytes plus both headers is what a packet needs to carry the tag its pong is matched by,
    /// so a floor under that would probe sizes no answer could come back from.
    /// </summary>
    [Fact]
    public void TheOutboundTestRefusesWhatItCannotProbe()
    {
        Assert.Equal(8u + SenkushaMeasurements.PingDataAdd, SenkushaMeasurements.OutboundFloor);
        Assert.Equal(0x2eu, SenkushaMeasurements.PingDataAdd);

        Assert.True(SenkushaMeasurements.OutboundArgumentsAccepted(1200, 576, 1454));

        // A floor under the packet's own overhead.
        Assert.False(SenkushaMeasurements.OutboundArgumentsAccepted(1200, 8, 1454));

        // An inverted window, and an inbound answer outside it either way.
        Assert.False(SenkushaMeasurements.OutboundArgumentsAccepted(1200, 1454, 576));
        Assert.False(SenkushaMeasurements.OutboundArgumentsAccepted(400, 576, 1454));
        Assert.False(SenkushaMeasurements.OutboundArgumentsAccepted(2000, 576, 1454));

        if (Source() is not { } source)
            return;

        Assert.Contains(
            $"#define MTU_UDP_PACKET_ADD 0x{SenkushaMeasurements.UdpPacketAdd:x}",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A ping is a v7 AV packet with a codec no decoder sees, and its padding says CHIAKI.
    ///
    /// The bytes are the measurement: the outbound test fills a packet to the size it is probing,
    /// and what it fills it with is six ASCII characters repeating. Worth pinning because a port
    /// that padded with zeroes would be sending something a middlebox may treat differently.
    /// </summary>
    [Fact]
    public void APingIsAV7PacketPaddedWithTheProjectsName()
    {
        Assert.Equal(0xff, SenkushaMeasurements.PingCodec);
        Assert.Equal(0x800, SenkushaMeasurements.PingUnitsInFrameTotal);
        Assert.Equal(0x224, SenkushaMeasurements.PingBufferBytes);
        Assert.Equal("CHIAKI"u8.ToArray(), SenkushaMeasurements.OutboundPadding.ToArray());

        // The AV add IS the v7 header's base size, which PP679 gave the formatter an owner for.
        Assert.Equal(0x12u, SenkushaMeasurements.AvPacketAdd);

        if (SanitizerSource.LocateRelative(@"lib\include\chiaki\takion.h") is not { } header)
            return;

        Assert.Contains(
            $"#define CHIAKI_TAKION_V7_AV_HEADER_SIZE_BASE\t\t\t\t\t0x{SenkushaMeasurements.AvPacketAdd:x}",
            File.ReadAllText(header),
            StringComparison.Ordinal);
    }
}
