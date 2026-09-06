using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What one ping of the round-trip test came to.</summary>
public enum SenkushaPing
{
    /// <summary>A pong arrived and its time counts toward the average.</summary>
    Answered,

    /// <summary>Nothing arrived in time. The loop CARRIES ON - a lost ping is not a failed test.</summary>
    Lost,

    /// <summary>Somebody stopped the session, which ends the test where it stands.</summary>
    Stopped,
}

/// <summary>What a whole round-trip test came to.</summary>
/// <param name="Outcome">Success where any ping was answered at all.</param>
/// <param name="Answered">How many of the ten came back.</param>
/// <param name="AverageMicroseconds">The mean over the ANSWERED pings, not over the count sent.</param>
public readonly record struct SenkushaRttReading(
    ChiakiError Outcome, int Answered, ulong AverageMicroseconds);

/// <summary>One step of an MTU search, and which way it moved the window.</summary>
/// <param name="Probed">The size this step asked about.</param>
/// <param name="Carried">Whether any of the attempts came back.</param>
/// <param name="Min">The window's floor after the step.</param>
/// <param name="Max">And its ceiling.</param>
public readonly record struct SenkushaMtuStep(uint Probed, bool Carried, uint Min, uint Max);

/// <summary>
/// PP789, under PP784: senkusha's three measurements, which are the numbers the launch spec spends.
///
/// Senkusha exists to measure a link and stop. PP788 gave the states it walks doing it; this is
/// what the walking is FOR - a round trip over ten pings, an inbound MTU and an outbound one, each
/// bisecting between 576 and 1454 with three attempts per step.
///
/// THE AVERAGE IS OVER THE PINGS THAT CAME BACK. Ten go out and the mean divides by how many
/// answered, not by ten - so a link losing half its pings reports the round trip of the half that
/// survived rather than double it. A lost ping is tolerated and the loop carries on; only a stop
/// ends the test early, and only a test that got NOTHING back fails.
///
/// THE TIMEOUT THE MTU TESTS USE IS DERIVED AND CLAMPED. Not EXPECT_TIMEOUT_MS: the round trip
/// times five, in milliseconds, held between 5 and 500. A port reaching for the five-second
/// constant here would spend twenty-five seconds probing what the C does in a tenth of one.
///
/// AND THE TWO SEARCHES ARE NOT SYMMETRIC, which is the part a reader tidies. The inbound test
/// starts at the CEILING, so a link that carries 1454 is measured in one probe. The outbound test
/// starts at whatever the inbound one found. Both answer with the window's floor when it closes -
/// and the outbound test's closing message reports the CEILING as its request, which is the number
/// it stopped being able to carry rather than the one it can.
/// </summary>
public static class SenkushaMeasurements
{
    /// <summary>SENKUSHA_PING_COUNT_DEFAULT, which is how many the round-trip test sends.</summary>
    public const int PingCount = 10;

    /// <summary>The floor both MTU searches are given.</summary>
    public const uint MtuMin = 576;

    /// <summary>And the ceiling, which is where the inbound search starts.</summary>
    public const uint MtuMax = 1454;

    /// <summary>How many attempts one step of a search is given before it is called lost.</summary>
    public const uint MtuRetries = 3;

    /// <summary>MTU_UDP_PACKET_ADD: an IPv4 header and a UDP one, which a payload is not.</summary>
    public const uint UdpPacketAdd = 0x1c;

    /// <summary>MTU_AV_PACKET_ADD, which is CHIAKI_TAKION_V7_AV_HEADER_SIZE_BASE.</summary>
    public const uint AvPacketAdd = 0x12;

    /// <summary>What a ping's payload is short of the IP packet it becomes.</summary>
    public const uint PingDataAdd = UdpPacketAdd + AvPacketAdd;

    /// <summary>The buffer one round-trip ping is sent from, zeroed before the header goes in.</summary>
    public const int PingBufferBytes = 0x224;

    /// <summary>Where the ping's tag is written, past the header the formatter wrote.</summary>
    public const int TagOffsetPastHeader = 4;

    /// <summary>units_in_frame_total on every ping, which is a constant and not a count.</summary>
    public const int PingUnitsInFrameTotal = 0x800;

    /// <summary>The codec byte a ping carries, which no decoder ever sees.</summary>
    public const byte PingCodec = 0xff;

    /// <summary>The bytes the outbound test pads its packet with, repeating.</summary>
    public static ReadOnlySpan<byte> OutboundPadding => "CHIAKI"u8;

    /// <summary>The lowest MTU the outbound test will accept as a floor.</summary>
    public static uint OutboundFloor => 8 + PingDataAdd;

    /// <summary>
    /// The timeout the two MTU tests use, from the round trip the first test measured.
    ///
    /// Five times the round trip, in milliseconds, clamped either side. The clamp is what makes
    /// this worth a function: a link measuring under a millisecond would otherwise be given a
    /// timeout of zero, and one measuring a tenth of a second would be given half a second more
    /// than it needs on every one of its steps.
    /// </summary>
    public static ulong MtuTimeoutMs(ulong roundTripMicroseconds)
    {
        ulong derived = roundTripMicroseconds * 5 / 1000;

        return Math.Clamp(derived, 5, 500);
    }

    /// <summary>
    /// The average a round-trip test reports, and what it reports where nothing came back.
    /// </summary>
    /// <param name="answeredMicroseconds">One reading per pong that arrived, in order.</param>
    public static SenkushaRttReading Average(IReadOnlyList<ulong> answeredMicroseconds)
    {
        ArgumentNullException.ThrowIfNull(answeredMicroseconds);

        if (answeredMicroseconds.Count == 0)
        {
            // Not a timeout and not canceled: the C answers UNKNOWN for a test that ran and
            // measured nothing, which is a different thing from one that was stopped.
            return new SenkushaRttReading(ChiakiError.Unknown, 0, 0);
        }

        ulong total = 0;
        foreach (ulong one in answeredMicroseconds)
            total += one;

        // Divided by the ANSWERED count. Ten were sent and the mean is over what returned.
        return new SenkushaRttReading(
            ChiakiError.Success, answeredMicroseconds.Count, total / (ulong)answeredMicroseconds.Count);
    }

    /// <summary>Where the inbound search's first probe goes, which is the ceiling.</summary>
    public static uint FirstInboundProbe(uint max) => max;

    /// <summary>
    /// And the outbound search's, which is whatever the inbound one found.
    ///
    /// Stated apart from the one above because they differ, and the difference is the whole reason
    /// the outbound test takes mtu_in as an argument at all.
    /// </summary>
    public static uint FirstOutboundProbe(uint mtuIn) => mtuIn;

    /// <summary>
    /// One step's effect on the window: a size that carried raises the floor, one that did not
    /// lowers the ceiling, and the next probe is the midpoint of what is left.
    /// </summary>
    public static SenkushaMtuStep Step(uint probed, bool carried, uint min, uint max)
    {
        uint nextMin = carried ? probed : min;
        uint nextMax = carried ? max : probed;

        return new SenkushaMtuStep(probed, carried, nextMin, nextMax);
    }

    /// <summary>The probe a window's step takes next, which is its midpoint rounded down.</summary>
    public static uint NextProbe(uint min, uint max) => min + ((max - min) / 2);

    /// <summary>Whether a search's window is still open, which is a gap of more than one.</summary>
    public static bool Searching(uint min, uint max) => max - min > 1;

    /// <summary>
    /// What the outbound test refuses before it sends anything.
    ///
    /// Four conditions and they are one guard in the C. The floor is not arbitrary: a packet
    /// smaller than eight bytes plus the two headers cannot carry the tag the pong is matched by.
    /// </summary>
    public static bool OutboundArgumentsAccepted(uint mtuIn, uint min, uint max)
        => min >= OutboundFloor && max >= min && mtuIn >= min && mtuIn <= max;

    /// <summary>
    /// The whole inbound search over a link that carries up to a given size, as a list of steps.
    ///
    /// The model's own arithmetic rather than a transcript: what it answers is which sizes get
    /// probed and what the search settles on, which is the half a port can get wrong without any
    /// message on the wire looking different.
    /// </summary>
    public static IReadOnlyList<SenkushaMtuStep> InboundSearch(uint carries, uint min = MtuMin, uint max = MtuMax)
    {
        var steps = new List<SenkushaMtuStep>();
        uint cur = FirstInboundProbe(max);

        while (Searching(min, max))
        {
            SenkushaMtuStep step = Step(cur, cur <= carries, min, max);
            steps.Add(step);

            min = step.Min;
            max = step.Max;
            cur = NextProbe(min, max);
        }

        return steps;
    }

    /// <summary>What a completed search answers, which is the window's floor and never its probe.</summary>
    public static uint Settled(IReadOnlyList<SenkushaMtuStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        return steps.Count == 0 ? MtuMin : steps[^1].Min;
    }
}

/// <summary>
/// PP789: the three measurements read out of senkusha.c, so the model cannot drift off the file.
/// </summary>
public static class SenkushaMeasurementsSource
{
    /// <summary>Where they are.</summary>
    public const string RelativePath = SenkushaStatesSource.RelativePath;

    /// <summary>senkusha.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>One of the three tests' bodies, or null where it is gone.</summary>
    public static string? TestBody(string source, string which)
        => CFunction.Body(source, $"static ChiakiErrorCode senkusha_run_{which}(");

    /// <summary>
    /// Whether the round trip is still averaged over the pings that ANSWERED.
    ///
    /// The divisor is the claim. Dividing by the count sent would report a link losing half its
    /// pings as twice as slow as it is, and the launch spec spends that number.
    /// </summary>
    public static bool TheAverageStillDividesByTheAnswered(string rttBody)
    {
        ArgumentNullException.ThrowIfNull(rttBody);

        return rttBody.Contains("*rtt_us = rtt_us_acc / pings_successful;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a lost pong still carries on rather than ending the test.
    ///
    /// The `continue` is what makes ten pings a measurement rather than ten chances to fail. A stop
    /// is the one thing that returns, and it returns CANCELED.
    /// </summary>
    public static bool ALostPongStillContinues(string rttBody)
    {
        ArgumentNullException.ThrowIfNull(rttBody);

        int missing = rttBody.IndexOf("if(!senkusha->state_finished)", StringComparison.Ordinal);
        if (missing < 0)
            return false;

        int canceled = rttBody.IndexOf("return CHIAKI_ERR_CANCELED;", missing, StringComparison.Ordinal);
        int carried = rttBody.IndexOf("continue;", missing, StringComparison.Ordinal);

        return canceled > missing && carried > canceled;
    }

    /// <summary>
    /// Whether the MTU timeout is still five round trips clamped between 5 and 500.
    ///
    /// Read from the run rather than from a test: the derivation happens once, between the RTT test
    /// and the two searches, and both searches are handed the answer.
    /// </summary>
    public static bool TheTimeoutIsStillFiveRoundTripsClamped(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        return runBody.Contains("mtu_timeout_ms = (*rtt_us * 5) / 1000;", StringComparison.Ordinal)
            && runBody.Contains("if(mtu_timeout_ms < 5)", StringComparison.Ordinal)
            && runBody.Contains("if(mtu_timeout_ms > 500)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two searches still start in different places, which is the asymmetry.
    ///
    /// Inbound at the ceiling and outbound at whatever inbound found. A port that started both at
    /// the ceiling would probe a size the link has already been shown not to carry.
    /// </summary>
    public static bool TheSearchesStillStartApart(string inBody, string outBody)
    {
        ArgumentNullException.ThrowIfNull(inBody);
        ArgumentNullException.ThrowIfNull(outBody);

        return inBody.Contains("uint32_t cur = max;", StringComparison.Ordinal)
            && outBody.Contains("uint32_t cur = mtu_in;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether both searches still answer with the window's floor.
    ///
    /// `*mtu = min` in each. The probe that succeeded and the floor are the same number by then,
    /// and a port answering with `cur` would be right until a search ended on a failure.
    /// </summary>
    public static bool BothStillAnswerWithTheFloor(string inBody, string outBody)
    {
        ArgumentNullException.ThrowIfNull(inBody);
        ArgumentNullException.ThrowIfNull(outBody);

        return inBody.Contains("*mtu = min;", StringComparison.Ordinal)
            && outBody.Contains("*mtu = min;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the outbound test's closing command still reports the CEILING as its request.
    ///
    /// The answer is min and the message carries max, which reads like a slip and is what the C
    /// sends. Reproduced rather than tidied - a console told the floor instead would be told
    /// something this port invented.
    /// </summary>
    public static bool TheClosingCommandStillReportsTheCeiling(string outBody)
    {
        ArgumentNullException.ThrowIfNull(outBody);

        int settled = outBody.IndexOf("*mtu = min;", StringComparison.Ordinal);
        if (settled < 0)
            return false;

        return outBody.IndexOf("client_mtu_cmd.mtu_req = max;", settled, StringComparison.Ordinal) > settled;
    }

    /// <summary>Whether a send that failed is still retried rather than returned, in the out test.</summary>
    public static bool AFailedSendStillRetriesOutbound(string outBody)
    {
        ArgumentNullException.ThrowIfNull(outBody);

        int sent = outBody.IndexOf("Senkusha failed to send ping", StringComparison.Ordinal);
        if (sent < 0)
            return false;

        return outBody.IndexOf("err = CHIAKI_ERR_TIMEOUT;", sent, StringComparison.Ordinal) > sent;
    }
}
