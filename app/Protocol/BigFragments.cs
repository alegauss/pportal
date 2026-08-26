namespace ChiakiNg.Protocol;

/// <summary>One message in a fragmented BIG.</summary>
/// <param name="Offset">Where in the encoded protobuf this fragment starts.</param>
/// <param name="Size">How many payload bytes it carries.</param>
/// <param name="IsFirst">Whether it is the first message rather than a continuation.</param>
/// <param name="EndsTheMessage">
/// Whether it carries the end-of-message flag. Exactly one fragment does, and it is the last.
/// </param>
public readonly record struct BigFragment(int Offset, int Size, bool IsFirst, bool EndsTheMessage);

/// <summary>
/// PP376: how a BIG is cut into messages, and which of them says the message is over.
///
/// BIG is the only message streamconnection.c fragments, and the one the console needs whole before
/// it will answer BANG - it carries the launch spec, the session key and the ECDH key. The transport
/// costs 26 bytes of overhead on a first message and 25 on a continuation, so a message can carry
/// `mtu - 26` or `mtu - 25` payload bytes respectively.
///
/// THE OVERHEAD TESTED HAS TO MATCH THE MESSAGE THAT WOULD CARRY THE REMAINDER. The C tested both at
/// once - `(mtu &lt; total + 26) || (mtu &lt; total + 25 &amp;&amp; !first)` - so on a continuation it used the
/// weaker of the two, entered on a remainder of exactly `mtu - 25`, and consumed all of it. That left
/// nothing for the trailing send, which is the ONLY send passing 1 as the end-of-message flag: the
/// console waited for a continuation that was never coming, the client waited for BANG, and neither
/// had an error to show. Whether it happened was decided by the encoded launch-spec length and the
/// MTU senkusha measured, which is why it survived every working setup and broke particular ones
/// reproducibly.
///
/// So the invariant here is not about sizes. It is that EVERY PLAN ENDS IN A FRAGMENT THAT ENDS THE
/// MESSAGE, whatever the payload size and whatever the MTU.
/// </summary>
public static class BigFragments
{
    /// <summary>Transport overhead on a first message.</summary>
    public const int FirstOverhead = 26;

    /// <summary>Transport overhead on a continuation.</summary>
    public const int ContinuationOverhead = 25;

    /// <summary>What senkusha subtracts for network overhead before any of this.</summary>
    public const int NetworkOverhead = 50;

    /// <summary>
    /// The narrowest MTU senkusha will report. Its search is bounded to [576, 1454], and the fallback
    /// when it fails outright is 1454 - which is why the `mtu - 26` arithmetic below cannot underflow
    /// in practice and the defect this models is the loop's condition rather than its subtraction.
    /// </summary>
    public const int NarrowestMeasuredMtu = 576;

    /// <summary>
    /// How the encoded BIG is cut up, in order.
    /// </summary>
    /// <param name="totalSize">Bytes the protobuf encoder wrote.</param>
    /// <param name="mtu">The MTU already reduced by <see cref="NetworkOverhead"/>.</param>
    public static IReadOnlyList<BigFragment> Plan(int totalSize, int mtu)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(mtu, FirstOverhead + 1);

        List<BigFragment> fragments = [];

        int remaining = totalSize;
        int offset = 0;
        bool first = true;

        // A fragment is taken only while the remainder does not fit in the message that would carry
        // it - so a strict remainder is always left for the terminator.
        while (mtu < remaining + (first ? FirstOverhead : ContinuationOverhead))
        {
            int size = mtu - (first ? FirstOverhead : ContinuationOverhead);
            fragments.Add(new BigFragment(offset, size, first, EndsTheMessage: false));

            offset += size;
            remaining -= size;
            first = false;
        }

        fragments.Add(new BigFragment(offset, remaining, first, EndsTheMessage: true));
        return fragments;
    }
}

/// <summary>PP376: the loop this reproduces, held against streamconnection.c.</summary>
public static class BigFragmentsSource
{
    /// <summary>Where the loop lives.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => Session.SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the remainder is still tested against the overhead of the message that would carry it.
    ///
    /// Stated as the condition that has to be there rather than as the absence of the old one, so an
    /// empty file answers no.
    /// </summary>
    public static bool TheRemainderIsTestedAgainstTheRightOverhead(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "while(first ? (mtu < total_size + 26) : (mtu < total_size + 25))", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the terminator is still the trailing send, which is what makes the condition
    /// load-bearing rather than cosmetic.
    ///
    /// The loop's sends pass 0 as the flag and the two after it pass 1. If a send inside the loop ever
    /// carries the flag, the reasoning above stops applying and this check has to be rewritten rather
    /// than trusted.
    /// </summary>
    public static bool TheTerminatorIsStillTheTrailingSend(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Session.CFunction.Body(core, "static ChiakiErrorCode stream_connection_send_big(");
        if (body is null)
            return false;

        int loop = body.IndexOf("while(first ?", StringComparison.Ordinal);
        int trailing = body.IndexOf("if(total_size > 0)", StringComparison.Ordinal);
        if (loop < 0 || trailing < 0 || trailing < loop)
            return false;

        // Inside the loop: both sends non-final. After it: both final.
        string inside = body[loop..trailing];
        bool loopSendsAreNonFinal =
            inside.Contains("send_message_data(&stream_connection->takion, 0, 1,", StringComparison.Ordinal)
            && inside.Contains("send_message_data_cont(&stream_connection->takion, 0, 1,", StringComparison.Ordinal)
            && !inside.Contains("takion, 1, 1,", StringComparison.Ordinal);

        string after = body[trailing..];
        bool trailingSendsAreFinal =
            after.Contains("send_message_data(&stream_connection->takion, 1, 1,", StringComparison.Ordinal)
            && after.Contains("send_message_data_cont(&stream_connection->takion, 1, 1,", StringComparison.Ordinal);

        return loopSendsAreNonFinal && trailingSendsAreFinal;
    }
}
