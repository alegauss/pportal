using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Why an assert about data is or is not a bound in front of a read.</summary>
public enum AssertVerdict
{
    /// <summary>
    /// A read follows it and nothing else bounds the value. This is the shape PP357 named: the
    /// shipped build compiles the assert out and the read runs anyway.
    /// </summary>
    Bound,

    /// <summary>
    /// A read follows it, and something else already guarantees the value - a caller that
    /// constructs it, or a guard one layer up. The assert restates what is already true.
    /// </summary>
    GuardedElsewhere,

    /// <summary>Nothing reads past it. It states a fact rather than protecting a access.</summary>
    Invariant,

    /// <summary>
    /// It says which caller reached here. A violation is a dispatch bug rather than an access past
    /// an end.
    /// </summary>
    Precondition,
}

/// <summary>One assert, where it is, and what reading it settled.</summary>
/// <param name="File">Repository-relative, with backslashes.</param>
/// <param name="Expression">The assert's condition, as the C spells it.</param>
/// <param name="Verdict">What it turned out to be.</param>
/// <param name="Because">What makes it that, in one sentence a reader can check.</param>
public readonly record struct DataAssert(
    string File, string Expression, AssertVerdict Verdict, string Because);

/// <summary>
/// PP369: the eight asserts that carry weight about data, each read and each settled.
///
/// PP357 established that an assert is not a bound in this project, because Release is built with
/// -DNDEBUG. It fixed the two in ctrl.c that guarded a copy and left a check that reads ctrl.c and
/// nothing else. PP369 named the other eight: four size assertions in takion.c, one guarding an
/// ECDH secret, one a pointer dereferenced two lines later, one an offset in senkusha.c.
///
/// SEVEN OF THE EIGHT ARE NOT BOUNDS, and that is the result rather than a disappointment. Three
/// have a read behind them and something else that already guarantees the value; three have nothing
/// reading past them at all; one states which caller arrived. Recorded with the reason for each, so
/// the next reader does not audit them again - which is what PP404's census did for error codes and
/// PP406 refined by asking whether the callee could fail.
///
/// THE EIGHTH IS REAL AND WAS REACHABLE FROM THE WIRE. takion_recv_message_init_ack asserted its
/// payload was 0x30 bytes and then read six fields and a 32-byte cookie out of it.
/// takion_parse_message ties payload_size to the datagram's own length, so a short INIT_ACK parsed,
/// passed the two checks above the assert, and was read 0x2c bytes past its end in the shipped
/// build. It is a check now.
///
/// THE CEILING IS THE POINT. <see cref="Bounds"/> counts what is left, and the suite holds it at
/// zero: an assert added in front of a read, in any of these five files, has to be argued for here
/// before it can ship.
/// </summary>
public static class DataAssertCensus
{
    /// <summary>The five files PP369 counted across.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        @"lib\src\session.c",
        @"lib\src\ctrl.c",
        @"lib\src\streamconnection.c",
        @"lib\src\takion.c",
        @"lib\src\senkusha.c",
    ];

    /// <summary>
    /// The eight, as reading each one settled it.
    ///
    /// Ordered as PP369's section lists them, so the two can be read side by side.
    /// </summary>
    public static IReadOnlyList<DataAssert> Census { get; } =
    [
        new(@"lib\src\streamconnection.c", "!stream_connection->ecdh_secret",
            AssertVerdict.Invariant,
            "a malloc follows it, not a read: it says no secret has been derived twice, and PP415's "
                + "allocation check is what handles the malloc failing."),

        new(@"lib\src\session.c", "session->login_pin_entered && session->login_pin",
            AssertVerdict.GuardedElsewhere,
            "the pointer is passed to chiaki_ctrl_set_login_pin two lines down, and the wait above "
                + "sets the flag and the pointer together - PP345 is the task that made that pair "
                + "reportable."),

        new(@"lib\src\takion.c", "buf_size >= 0xc",
            AssertVerdict.GuardedElsewhere,
            "buf_size - 0xc follows it, and both callers build the buffer as 0xc plus a payload "
                + "size, so the subtraction cannot wrap."),

        new(@"lib\src\takion.c", "buf_size > 0",
            AssertVerdict.GuardedElsewhere,
            "buf[0] follows it, and takion_recv refuses a received size of zero or less with "
                + "CHIAKI_ERR_NETWORK before the caller ever reaches this."),

        new(@"lib\src\takion.c", "msg.payload_size == 0x10 + TAKION_COOKIE_SIZE",
            AssertVerdict.Bound,
            "six reads and a 32-byte cookie copy follow it, and payload_size comes off the wire - "
                + "this is the one PP369 turned into a check."),

        new(@"lib\src\takion.c", "msg.payload_size == 0",
            AssertVerdict.Invariant,
            "the function returns on the next line: there is nothing behind it to protect."),

        new(@"lib\src\takion.c",
            "base_type == TAKION_PACKET_TYPE_VIDEO || base_type == TAKION_PACKET_TYPE_AUDIO",
            AssertVerdict.Precondition,
            "it says which arm of the dispatch called this, and a violation would be a packet "
                + "handled as audio-video rather than a read past an end."),

        new(@"lib\src\senkusha.c", "header_size == MTU_AV_PACKET_ADD",
            AssertVerdict.Invariant,
            "eight bytes are written at packet_buf + MTU_AV_PACKET_ADD, and that offset is a "
                + "constant the buffer was allocated and memset around - so a wrong header_size "
                + "would overwrite the header rather than leave the buffer."),
    ];

    /// <summary>
    /// How many of the eight are bounds with nothing else behind them.
    ///
    /// Zero, since PP369 turned the one into a check. The ratchet rule: it may fall and may not
    /// rise, so an assert added in front of an unguarded read turns the suite red in the commit
    /// that adds it.
    /// </summary>
    public const int Bounds = 0;

    /// <summary>Every assert in the census with a given verdict.</summary>
    public static IReadOnlyList<DataAssert> With(AssertVerdict verdict)
        => [.. Census.Where(entry => entry.Verdict == verdict)];

    /// <summary>
    /// Whether one census entry's assert is still in the file it names.
    ///
    /// A census that drifts from the files is worse than none: it reads as eight settled questions
    /// about code that has moved. An entry whose assert is gone is reported, not tolerated - except
    /// the <see cref="AssertVerdict.Bound"/> one, which is gone BECAUSE it was fixed.
    /// </summary>
    public static bool StillPresent(string source, DataAssert entry)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.Compact(CCall.Code(source))
            .Contains(CCall.Compact($"assert({entry.Expression})"), StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the one that was a bound is a CHECK now, in the file it lived in.
    ///
    /// The condition inverted, a log, and a return - the shape the three tests above it in that
    /// function already had.
    /// </summary>
    public static bool TheBoundIsNowACheck(string takionCore)
    {
        ArgumentNullException.ThrowIfNull(takionCore);

        string code = CCall.Compact(CCall.Code(takionCore));

        DataAssert bound = With(AssertVerdict.Bound)[0];

        // The assert is gone...
        if (code.Contains(CCall.Compact($"assert({bound.Expression})"), StringComparison.Ordinal))
            return false;

        // ...and the check is there, with the return behind it.
        int guard = code.IndexOf(
            CCall.Compact("if(msg.payload_size != 0x10 + TAKION_COOKIE_SIZE)"),
            StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int refuses = code.IndexOf(
            CCall.Compact("return CHIAKI_ERR_INVALID_RESPONSE;"), guard, StringComparison.Ordinal);
        int reads = code.IndexOf(
            CCall.Compact("uint8_t *pl = msg.payload;"), guard, StringComparison.Ordinal);

        return refuses > guard && reads > refuses;
    }

    /// <summary>One of the five files, or null outside a checkout.</summary>
    public static string? Locate(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return SanitizerSource.LocateRelative(relativePath);
    }
}
