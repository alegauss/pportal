using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>The states senkusha's machine declares, in the order senkusha.c declares them.</summary>
public enum SenkushaState
{
    /// <summary>Where init leaves it, and where nothing waits.</summary>
    Idle,

    /// <summary>Waiting for the takion to come up, which is the only thirty-second wait.</summary>
    TakionConnect,

    /// <summary>
    /// DECLARED AND NEVER ENTERED. Nothing assigns it and nothing compares against it.
    ///
    /// Kept because the C keeps it: a port that dropped the member would renumber every state
    /// after it, and the enum's values are what a reader of senkusha.c matches against. What it
    /// costs to keep is one row here saying it is unreachable, which is cheaper than a model whose
    /// numbering disagrees with the file it is about.
    /// </summary>
    ExpectStreaminfoAck,

    /// <summary>Waiting for the console's bang, which is what keys nothing here - senkusha is unencrypted.</summary>
    ExpectBang,

    /// <summary>Waiting for the ack of a data message, which is the state every send-and-wait enters.</summary>
    ExpectDataAck,

    /// <summary>Waiting for the protocol version to be acknowledged.</summary>
    ExpectProtocolAck,

    /// <summary>Waiting for a pong, which both the RTT test and the outbound MTU test do.</summary>
    ExpectPong,

    /// <summary>Waiting for the console's answer to an inbound MTU probe.</summary>
    ExpectMtu,

    /// <summary>Waiting for the console to ask for an outbound MTU test.</summary>
    ExpectClientMtuCommand,
}

/// <summary>The two flags senkusha's waits are decided by, as the C holds them.</summary>
/// <param name="Finished">state_finished, which is the only one the predicate reads.</param>
/// <param name="Failed">state_failed, which PP365 established nothing reads.</param>
/// <param name="ShouldStop">should_stop, which the predicate reads as well.</param>
public readonly record struct SenkushaWaitState(
    bool Finished = false, bool Failed = false, bool ShouldStop = false);

/// <summary>
/// PP788, under PP784: senkusha's state walk, which five models of the file had left out.
///
/// PP28's placement says where senkusha sits in session.c, PP380's outcomes say why one of its
/// waits came back, PP379's results say every send's answer is read, PP421 replays its handshake
/// and PP702 counts the takion symbols it calls. None of them answers which STATE the machine is
/// in or what ends the wait that state is making - which is the first thing a port of the run
/// needs, and the last thing prose can carry.
///
/// THE PREDICATE READS TWO FIELDS AND THERE ARE THREE. state_finished_cond_check answers
/// <c>state_finished || should_stop</c>, so a wait ends on the state finishing or on somebody
/// stopping the session, and NOT on the state failing. state_failed is written ten times and read
/// nowhere - PP365 found that and checks both files for it, so this reproduces the silence rather
/// than repairing it: a port that ended a wait on failure would report failures sooner than the C,
/// which is better behaviour and different behaviour.
///
/// ONE STATE IS UNREACHABLE. <see cref="SenkushaState.ExpectStreaminfoAck"/> is declared in the
/// enum and never assigned to, never compared against. Nine states and the machine can be in
/// eight, which is a fact about the file worth writing down once rather than rediscovering while
/// porting the run.
///
/// FOUR TIMEOUTS FOR SIX WAITS, and the fourth is computed. The connect gets thirty seconds, three
/// states get five, a pong gets one - and the two MTU waits use a value derived from the round
/// trip, which is PP789's subject and appears here only as the fact that they do not use a
/// constant.
/// </summary>
public static class SenkushaStates
{
    /// <summary>SENKUSHA_PORT, which is not the session's and not the stream's.</summary>
    public const int Port = 9297;

    /// <summary>CONNECT_TIMEOUT_MS, the longest wait in the file.</summary>
    public const int ConnectTimeoutMs = 30000;

    /// <summary>EXPECT_TIMEOUT_MS, which three of the states use.</summary>
    public const int ExpectTimeoutMs = 5000;

    /// <summary>EXPECT_PONG_TIMEOUT_MS, which the RTT test's own wait uses.</summary>
    public const int ExpectPongTimeoutMs = 1000;

    /// <summary>The protocol version senkusha's takion connects with, which is not the stream's nine.</summary>
    public const int ProtocolVersion = 7;

    /// <summary>Whether senkusha's takion encrypts, which it does not - so no key position is spent.</summary>
    public const bool EncryptsItsTakion = false;

    /// <summary>Every state the enum declares, in its own order.</summary>
    public static IReadOnlyList<SenkushaState> Declared { get; } = [.. Enum.GetValues<SenkushaState>()];

    /// <summary>
    /// The one the machine can never be in, which is what makes Declared and Reachable differ.
    /// </summary>
    public static SenkushaState Unreachable => SenkushaState.ExpectStreaminfoAck;

    /// <summary>The eight a run can actually enter.</summary>
    public static IReadOnlyList<SenkushaState> Reachable { get; } =
        [.. Declared.Where(one => one != Unreachable)];

    /// <summary>
    /// How long the wait in a state is given, or null where the state makes none.
    ///
    /// Null twice and for two different reasons: Idle waits for nothing, and the unreachable state
    /// has no wait because it has no entry. The two MTU states answer with the derived value, which
    /// this cannot state - so they answer null as well and <see cref="DerivesItsTimeout"/> is what
    /// tells the three cases apart.
    /// </summary>
    public static int? TimeoutOf(SenkushaState state) => state switch
    {
        SenkushaState.TakionConnect => ConnectTimeoutMs,
        SenkushaState.ExpectProtocolAck => ExpectTimeoutMs,
        SenkushaState.ExpectBang => ExpectTimeoutMs,
        SenkushaState.ExpectDataAck => ExpectTimeoutMs,
        SenkushaState.ExpectClientMtuCommand => ExpectTimeoutMs,
        SenkushaState.ExpectPong => ExpectPongTimeoutMs,
        _ => null,
    };

    /// <summary>
    /// Whether a state's wait is given a timeout computed from the round trip rather than a constant.
    ///
    /// Only the inbound MTU test's. The outbound one waits on a pong and uses the derived value
    /// too, which is why the pong's own constant above is not the whole answer for that state - the
    /// RTT test uses the constant and the MTU-out test does not, in the same state.
    /// </summary>
    public static bool DerivesItsTimeout(SenkushaState state)
        => state is SenkushaState.ExpectMtu or SenkushaState.ExpectPong;

    /// <summary>
    /// The predicate: finished or stopped, and never failed.
    ///
    /// state_finished_cond_check as the C spells it. The failure flag is written at every entry and
    /// by the connect arm of the callback, and this is the function that would have read it.
    /// </summary>
    public static bool WaitEnds(SenkushaWaitState flags) => flags.Finished || flags.ShouldStop;

    /// <summary>PP365's finding, stated so a repair upstream turns a check red rather than passing.</summary>
    public static bool FailureFlagIsRead => false;

    /// <summary>What a state entry leaves behind, which is the C's own two lines after the assignment.</summary>
    public static SenkushaWaitState Entering(SenkushaWaitState flags)
        => flags with { Finished = false, Failed = false };
}

/// <summary>
/// PP788: the walk held against senkusha.c, so a model of it cannot drift off the file.
/// </summary>
public static class SenkushaStatesSource
{
    /// <summary>Where the machine is.</summary>
    public const string RelativePath = @"lib\src\senkusha.c";

    /// <summary>senkusha.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The C's own name for each state, which is what a source check matches.</summary>
    public static string NameOf(SenkushaState state) => state switch
    {
        SenkushaState.Idle => "STATE_IDLE",
        SenkushaState.TakionConnect => "STATE_TAKION_CONNECT",
        SenkushaState.ExpectStreaminfoAck => "STATE_EXPECT_STREAMINFO_ACK",
        SenkushaState.ExpectBang => "STATE_EXPECT_BANG",
        SenkushaState.ExpectDataAck => "STATE_EXPECT_DATA_ACK",
        SenkushaState.ExpectProtocolAck => "STATE_EXPECT_PROTOCOL_ACK",
        SenkushaState.ExpectPong => "STATE_EXPECT_PONG",
        SenkushaState.ExpectMtu => "STATE_EXPECT_MTU",
        _ => "STATE_EXPECT_CLIENT_MTU_COMMAND",
    };

    /// <summary>Whether the file still declares a state at all, which is how a rename reads.</summary>
    public static bool Declares(string source, SenkushaState state)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Contains(NameOf(state), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a state is ever ASSIGNED, which is a different question from being declared.
    ///
    /// The assignment is what makes a state reachable, and the one this exists for answers no.
    /// Read over code with the comments stripped, because this file's own prose names the state
    /// it is about - and PP735's trap is that a paragraph explaining a symbol is not a use of it.
    /// </summary>
    public static bool IsEntered(string source, SenkushaState state)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.Code(source)
            .Contains($"senkusha->state = {NameOf(state)};", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the predicate still reads the two fields and not the third.
    ///
    /// The whole of PP365's finding in this file, and what a repair upstream would move: a
    /// predicate that started reading state_failed would end waits this port does not end.
    /// </summary>
    public static bool ThePredicateStillReadsTwoFields(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (CFunction.Body(source, "static bool state_finished_cond_check(") is not { } body)
            return false;

        return body.Contains("senkusha->state_finished || senkusha->should_stop", StringComparison.Ordinal)
            && !body.Contains("state_failed", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether every assignment of the state is still followed by both flags being cleared.
    ///
    /// The C's triple, which PP773 found the stream connection's port carrying only two thirds of.
    /// Counted rather than sampled: a single entry that stopped clearing is a state that begins
    /// with the last one's answer already in it.
    ///
    /// A FILE WITH NO ENTRIES ANSWERS NO, which is the convention every drift check here follows
    /// and is the honest reading besides: nothing to check is not the same as everything checking
    /// out, and a source that stopped assigning the state has lost the machine rather than kept it.
    /// </summary>
    public static bool EveryEntryStillClearsBothFlags(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (EntryCount(source) == 0)
            return false;

        string[] lines = CCall.Code(source).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("senkusha->state = STATE_", StringComparison.Ordinal))
                continue;

            if (i + 2 >= lines.Length
                || !lines[i + 1].Contains("state_finished = false", StringComparison.Ordinal)
                || !lines[i + 2].Contains("state_failed = false", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// How many times the file clears the pair, which is what a count of entries is checked against.
    /// </summary>
    public static int EntryCount(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.Code(source)
            .Split("senkusha->state = STATE_", StringSplitOptions.None)
            .Length - 1;
    }
}
