using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One STUN server's answer: where this end looked like it was, and on which port.</summary>
public readonly record struct StunResponse(string Address, ushort Port);

/// <summary>
/// What the allocation test concluded.
/// </summary>
/// <param name="Address">The external address to offer, which is the LAST one that answered.</param>
/// <param name="Port">The port to offer - see <see cref="PortAllocationTest"/> for the look-ahead.</param>
/// <param name="Increment">How far the NAT moves the port between allocations, as far as anyone can tell.</param>
/// <param name="RandomAllocation">Set when two ways of calculating that disagreed.</param>
public readonly record struct PortAllocation(
    string Address, ushort Port, int Increment, bool RandomAllocation);

/// <summary>
/// PP33: guessing how a NAT hands out ports, from up to four STUN answers.
///
/// This is the widest decision tree in the hole punching, and reading it is not the same as knowing
/// what it does - which is the whole reason it is worth porting into something a test can drive.
/// Four things it does that nobody would predict from the outside:
///
///   THE LOOK-AHEAD SHRINKS AS THE EVIDENCE GROWS. With two answers the offered port is
///   <c>port2 + 2 * increment</c>; with three it is <c>port3 + increment</c>; with FOUR it is
///   <c>port4</c>, with no increment added at all. So the case that knows the most about the NAT is
///   the only one that does not use what it learned, and a port that "completed the pattern" by
///   adding the increment to the four-response case would offer a different port than the Qt client
///   on exactly the networks where the measurement worked best.
///
///   ONE LEAF SUBTRACTS THE WRONG WAY ROUND. Where the first two addresses differ, the second
///   matches neither of the last two, and the last two match each other, the increment is
///   <c>port3 - port4</c>. Every other leaf in the tree is later-minus-earlier. On a NAT whose ports
///   climb, that leaf alone produces a negative increment.
///
///   THE SAME SUBTRACTION IS SIGNED IN THREE PLACES AND UNSIGNED IN THE FOURTH. Every increment is
///   an int32_t except in the all-four-addresses-agree leaf, where the three are uint16_t - so a
///   port that went DOWN wraps to about sixty-five thousand there and is simply negative
///   everywhere else. Two answers to the same question, decided by which branch you reached.
///
///   AVERAGES ARE TAKEN ACROSS ADDRESSES THE CODE JUST FOUND DIFFERENT. <c>(port4 - port1) / 4</c>
///   and <c>(port3 - port1) / 2</c> both appear in branches reached only because addr1 did not
///   match addr2 - so port1 is a measurement of a different external address, averaged in anyway.
///
/// All four are reproduced. Every one of them is a real number this client puts in a real offer,
/// which is the line PP194 drew: a latent overflow is not behaviour, but a wrong-looking number
/// that gets sent is.
/// </summary>
public static class PortAllocationTest
{
    /// <summary>How many answers the test can use. The fifth server is never asked.</summary>
    public const int MaxResponses = 4;

    /// <summary>
    /// The conclusion, or null when nothing answered at all - which is the one case that fails
    /// rather than producing a guess.
    /// </summary>
    public static PortAllocation? Test(IReadOnlyList<StunResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);

        return responses.Count switch
        {
            0 => null,
            1 => One(responses),
            2 => Two(responses),
            3 => Three(responses),
            _ => Four(responses),
        };
    }

    /// <summary>Whether two answers came from the same external address.</summary>
    private static bool Same(IReadOnlyList<StunResponse> r, int a, int b)
        => string.Equals(r[a].Address, r[b].Address, StringComparison.Ordinal);

    /// <summary>The port to offer, truncated the way the core's uint16_t assignment truncates.</summary>
    private static ushort Offer(int port) => unchecked((ushort)port);

    /// <summary>One answer says nothing about movement, so the increment is zero.</summary>
    private static PortAllocation One(IReadOnlyList<StunResponse> r)
        => new(r[0].Address, r[0].Port, 0, false);

    /// <summary>Two answers, and a port offered TWO increments ahead.</summary>
    private static PortAllocation Two(IReadOnlyList<StunResponse> r)
    {
        int increment = Same(r, 0, 1) ? r[1].Port - r[0].Port : 0;
        return new(r[1].Address, Offer(r[1].Port + (2 * increment)), increment, false);
    }

    /// <summary>Three answers, and a port offered ONE increment ahead.</summary>
    private static PortAllocation Three(IReadOnlyList<StunResponse> r)
    {
        int increment;
        bool random = false;

        if (!Same(r, 0, 1))
        {
            if (Same(r, 0, 2))
                increment = (r[2].Port - r[0].Port) / 2;
            else if (Same(r, 1, 2))
                increment = r[2].Port - r[1].Port;
            else
                increment = 0;
        }
        else if (!Same(r, 0, 2))
        {
            increment = r[1].Port - r[0].Port;
        }
        else
        {
            increment = r[1].Port - r[0].Port;
            Disagree(r[2].Port - r[1].Port, ref increment, ref random);
        }

        return new(r[2].Address, Offer(r[2].Port + increment), increment, random);
    }

    /// <summary>
    /// Four answers, and a port offered with NO increment at all - see the class note.
    /// </summary>
    private static PortAllocation Four(IReadOnlyList<StunResponse> r)
    {
        int increment = 0;
        bool random = false;

        if (!Same(r, 0, 1))
        {
            if (Same(r, 0, 2) || Same(r, 0, 3))
            {
                if (Same(r, 0, 3))
                {
                    if (Same(r, 0, 2))
                    {
                        increment = r[3].Port - r[2].Port;
                        Disagree((r[2].Port - r[0].Port) / 2, ref increment, ref random);
                    }
                    else
                    {
                        // Averaged over four, across an address the code just found different.
                        increment = (r[3].Port - r[0].Port) / 4;
                    }
                }
                else
                {
                    increment = (r[2].Port - r[0].Port) / 2;
                }
            }
            else if (Same(r, 1, 2) || Same(r, 1, 3))
            {
                if (Same(r, 1, 3))
                {
                    if (Same(r, 1, 2))
                    {
                        increment = r[3].Port - r[2].Port;
                        Disagree(r[2].Port - r[1].Port, ref increment, ref random);
                    }
                    else
                    {
                        increment = (r[3].Port - r[0].Port) / 4;
                    }
                }
                else
                {
                    increment = (r[2].Port - r[0].Port) / 2;
                }
            }
            else if (Same(r, 2, 3))
            {
                // BACKWARDS, and the only leaf in the tree that is - see the class note.
                increment = r[2].Port - r[3].Port;
            }
            else
            {
                increment = 0;
            }
        }
        else if (!Same(r, 1, 2))
        {
            increment = r[1].Port - r[0].Port;
            if (Same(r, 1, 3))
                Disagree((r[3].Port - r[1].Port) / 2, ref increment, ref random);
        }
        else if (!Same(r, 2, 3))
        {
            increment = r[1].Port - r[0].Port;
            Disagree(r[2].Port - r[1].Port, ref increment, ref random);
        }
        else
        {
            // The one leaf where the increments are UNSIGNED, so a port that went down wraps.
            ushort first = unchecked((ushort)(r[1].Port - r[0].Port));
            ushort second = unchecked((ushort)(r[2].Port - r[1].Port));
            ushort third = unchecked((ushort)(r[3].Port - r[2].Port));

            if (first == second && second == third)
            {
                increment = first;
            }
            else if (first == second || first == third)
            {
                increment = first;
            }
            else if (second == third)
            {
                increment = second;
            }
            else
            {
                random = true;
                increment = first != 0 ? first : second;
            }
        }

        return new(r[3].Address, r[3].Port, increment, random);
    }

    /// <summary>
    /// Two calculations of the same increment, disagreeing.
    ///
    /// The disagreement is what sets random allocation - and the SECOND figure is only adopted when
    /// the first came out zero, so a measured increment is never overwritten by a second opinion.
    /// </summary>
    private static void Disagree(int other, ref int increment, ref bool random)
    {
        if (other == increment)
            return;

        random = true;
        if (increment == 0)
            increment = other;
    }
}

/// <summary>
/// PP33: the allocation test's rules where the Qt core states them.
/// </summary>
public static class PortAllocationSource
{
    /// <summary>Where the test lives.</summary>
    public const string RelativePath = @"lib\src\remote\stun.h";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The three ports the tree offers, by how far ahead each looks.</summary>
    public static IReadOnlyList<string> Offers { get; } =
    [
        "*port = port2 + 2 * (*allocation_increment);",
        "*port = port3 + (*allocation_increment);",
        "*port = port4;",
    ];

    /// <summary>Whether the look-ahead still shrinks from two, to one, to none.</summary>
    public static bool TheLookAheadStillShrinks(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach (string offer in Offers)
        {
            if (!core.Contains(offer, StringComparison.Ordinal))
                return false;
        }

        // And the four-response case really does not add one, which is the surprising half.
        return !core.Contains("*port = port4 + ", StringComparison.Ordinal);
    }

    /// <summary>Whether the one backwards subtraction is still backwards.</summary>
    public static bool TheBackwardsLeafIsStillBackwards(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        // Stated as a pair: the backwards one is still there, and the forwards spelling of the same
        // two ports is still what every other leaf uses - so a fix upstream turns this red either
        // by removing the first or by leaving only the second.
        return core.Contains("*allocation_increment = port3 - port4;", StringComparison.Ordinal)
            && core.Contains("*allocation_increment = port4 - port3;", StringComparison.Ordinal);
    }

    /// <summary>Whether the four-agreeing leaf still measures with unsigned sixteen-bit values.</summary>
    public static bool TheAgreeingLeafIsStillUnsigned(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("uint16_t increment0 = port2 - port1;", StringComparison.Ordinal)
            && core.Contains("uint16_t increment1 = port3 - port2;", StringComparison.Ordinal)
            && core.Contains("uint16_t increment2 = port4 - port3;", StringComparison.Ordinal)
            && core.Contains("int32_t allocation_increment1", StringComparison.Ordinal);
    }

    /// <summary>Whether averages are still taken across addresses the branch found different.</summary>
    public static bool TheAveragesStillCrossAnAddressChange(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("*allocation_increment = (port4 - port1) / 4;", StringComparison.Ordinal)
            && core.Contains("*allocation_increment = (port3 - port1) / 2;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a second opinion still only counts when the first was zero - the rule that keeps a
    /// measured increment from being overwritten by a disagreeing one.
    /// </summary>
    public static bool TheSecondOpinionStillOnlyFillsAZero(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(*allocation_increment == 0)", StringComparison.Ordinal)
            && core.Contains("*allocation_increment = allocation_increment1;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the broken shuffle is still written out TWICE, spelled two different ways - which is
    /// why PP198's single check was not enough to cover the header.
    /// </summary>
    public static bool TheShuffleIsStillWrittenTwice(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("int j = 1 + chiaki_random_32() % (i - 1);", StringComparison.Ordinal)
            && core.Contains("int j = (chiaki_random_32() % (i - 1)) + 1;", StringComparison.Ordinal);
    }
}
