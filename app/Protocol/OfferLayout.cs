namespace ChiakiNg.Protocol;

/// <summary>Which shape the offer's candidate array ends up in.</summary>
public enum OfferShape
{
    /// <summary>No port guessing: the pair is shifted down to slots zero and one.</summary>
    Plain,

    /// <summary>An allocation may have slipped in: eight guesses, then the pair.</summary>
    GuessedEight,

    /// <summary>A forced count of guesses, then the pair.</summary>
    GuessedForced,

    /// <summary>A measured count of guesses, then the pair.</summary>
    GuessedMeasured,

    /// <summary>STUN produced nothing usable: three, with the pair at one and two.</summary>
    NoStun,
}

/// <summary>Where one candidate sits once the layout is settled.</summary>
/// <param name="Remote">The index the static or STUN-addressed candidate ends at.</param>
/// <param name="Local">The index the local candidate ends at.</param>
/// <param name="Count">What the message declares it is sending.</param>
public readonly record struct OfferSlots(int Remote, int Local, int Count);

/// <summary>
/// PP251: where the two candidates PP248 reads are born.
///
/// THE COUNT IS WRITTEN BEFORE THE LAYOUT EXISTS. Four slots are allocated, the count is set to two,
/// and the two real candidates are parked at slots one and two with slot zero held for a STUN
/// result. Every path afterwards corrects both - which is why <see cref="SlotsFor"/> takes the shape
/// rather than the port holding one layout and hoping.
///
/// WHAT IS HANDED FORWARD IS SLOT ZERO, WHATEVER ENDED UP THERE. The session keeps two candidates:
/// the local one, and whatever is in slot zero. A comment beside that line says slot zero is the
/// STUN candidate if there is one and the static one otherwise. That is true of the ADDRESS on every
/// path. It is false of the PORT on the guessing paths, where slot zero is the STUN address carrying
/// the FIRST GUESS - and the guessing deliberately steps past the real allocation before writing any
/// of them, so the actual STUN port is the one port slot zero never holds. See
/// <see cref="SlotZeroHoldsTheStunPort"/>.
///
/// PP248 reads those two back and fills the winner's mapped address and port from the second. Its
/// reading of which is which is confirmed here - <see cref="HeldByTheSession"/> is the mapping - and
/// what it takes for the mapped port is a guess whenever port guessing ran.
/// </summary>
public static class OfferLayout
{
    /// <summary>How many slots are allocated before anything knows how many are needed.</summary>
    public const int InitialSlots = 4;

    /// <summary>And the count written at that moment, before any path has run.</summary>
    public const int ProvisionalCount = 2;

    /// <summary>Where the pair is parked before the layout is decided.</summary>
    public static OfferSlots Provisional { get; } = new(Remote: 1, Local: 2, Count: ProvisionalCount);

    /// <summary>How many guesses the eight-guess path writes.</summary>
    public const int EightGuesses = 8;

    /// <summary>How many slots that path grows the array to - one more than it uses.</summary>
    public const int GrownSlots = 11;

    /// <summary>Where the pair ends up, and what the message declares.</summary>
    /// <param name="shape">Which path ran.</param>
    /// <param name="guesses">How many guesses, for the two counted shapes.</param>
    public static OfferSlots SlotsFor(OfferShape shape, int guesses = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(guesses);

        return shape switch
        {
            // Shifted down: remote to zero, local to one, and the count is already right.
            OfferShape.Plain => new OfferSlots(Remote: 0, Local: 1, Count: 2),

            OfferShape.GuessedEight => new OfferSlots(
                Remote: EightGuesses, Local: EightGuesses + 1, Count: EightGuesses + 2),

            OfferShape.GuessedForced or OfferShape.GuessedMeasured => new OfferSlots(
                Remote: guesses, Local: guesses + 1, Count: guesses + 2),

            // Nothing moved: the pair stays where it was parked, and the count grows to cover
            // slot zero, which is sent as it was left.
            _ => new OfferSlots(Remote: 1, Local: 2, Count: 3),
        };
    }

    /// <summary>Whether this shape put guesses in front of the pair.</summary>
    public static bool Guesses(OfferShape shape)
        => shape is OfferShape.GuessedEight or OfferShape.GuessedForced or OfferShape.GuessedMeasured;

    /// <summary>
    /// What the session keeps: the local candidate, then slot zero.
    ///
    /// PP248 names the second one for the remote end. It is this side's address as the outside sees
    /// it, which is what that name is about - and it is read out of slot zero, not out of the slot
    /// the remote candidate ended in.
    /// </summary>
    public static (int Local, int FromSlot) HeldByTheSession { get; } = (Local: 0, FromSlot: 0);

    /// <summary>
    /// Whether slot zero carries the port STUN actually reported.
    ///
    /// Only when nothing was guessed. The guessing writes its first candidate AFTER stepping the
    /// port forward, so the real allocation is skipped rather than kept as the first entry.
    /// </summary>
    public static bool SlotZeroHoldsTheStunPort(OfferShape shape) => !Guesses(shape);

    /// <summary>
    /// And whether it carries the STUN address, which it does on every path that had one.
    /// </summary>
    public static bool SlotZeroHoldsTheStunAddress(OfferShape shape) => shape != OfferShape.NoStun;

    /// <summary>What the comment beside that copy claims slot zero is.</summary>
    public const string WhatTheCommentClaims = "either STUN candidate if it exists, else STATIC candidate";

    /// <summary>
    /// The port the first guess carries, from the reported one and the step between guesses.
    ///
    /// The step is applied BEFORE the write, so the reported port is never one of the guesses.
    /// </summary>
    public static int FirstGuessedPort(int reportedPort, int increment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reportedPort);

        int stepped = reportedPort + increment;

        // Well-known ports are stepped over unless the allocation is already among them, which
        // implies the router uses them.
        if (stepped < 1024 && reportedPort > 1024)
            return ushort.MaxValue - (1024 - stepped);

        if (stepped < 1)
            return stepped + ushort.MaxValue;

        return stepped > ushort.MaxValue ? stepped - ushort.MaxValue + 1024 : stepped;
    }
}

/// <summary>
/// PP251: the layout where the core writes it.
/// </summary>
public static class OfferLayoutSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the count is still written before the layout is decided.</summary>
    public static bool TheCountIsStillWrittenFirst(string core)
    {
        string body = Body(core);

        int count = body.IndexOf(
            $"msg.conn_request->num_candidates = {OfferLayout.ProvisionalCount};", StringComparison.Ordinal);
        int allocated = body.IndexOf(
            $"msg.conn_request->candidates = calloc({OfferLayout.InitialSlots}, sizeof(Candidate));",
            StringComparison.Ordinal);
        int parked = body.IndexOf(
            $"Candidate *candidate_local = &msg.conn_request->candidates[{OfferLayout.Provisional.Local}];",
            StringComparison.Ordinal);

        return count >= 0 && allocated > count && parked > allocated;
    }

    /// <summary>And whether the pair is still parked at those two slots.</summary>
    public static bool ThePairIsStillParkedThere(string core)
        => Body(core).Contains(
            $"Candidate *candidate_remote = &msg.conn_request->candidates[{OfferLayout.Provisional.Remote}];",
            StringComparison.Ordinal);

    /// <summary>Whether the plain path still shifts the pair down onto slots zero and one.</summary>
    public static bool ThePlainPathStillShiftsDown(string core)
        => Body(core).Contains(
            """
            memcpy(&msg.conn_request->candidates[0], &msg.conn_request->candidates[1], sizeof(Candidate));
                                memcpy(&msg.conn_request->candidates[1], &msg.conn_request->candidates[2], sizeof(Candidate));
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>How many paths move the pair to the top with guesses in front of it. Three.</summary>
    public static int HowManyPathsPutGuessesFirst(string core)
        => Body(core).Split("&original_candidates[1], sizeof(Candidate));", StringSplitOptions.None).Length - 1;

    /// <summary>
    /// Whether the session still keeps the local candidate and slot zero - the mapping PP248 reads.
    /// </summary>
    public static bool TheSessionStillKeepsSlotZero(string core)
        => Body(core).Contains(
            """
            memcpy(&session->local_candidates[0], candidate_local, sizeof(Candidate));
                // either STUN candidate if it exists, else STATIC candidate
                memcpy(&session->local_candidates[1], &msg.conn_request->candidates[0], sizeof(Candidate));
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the guesses still take the STUN address and step the port before writing it - which
    /// is what leaves the reported port out of the array.
    /// </summary>
    public static bool TheGuessesStillStepBeforeWriting(string core)
    {
        string body = Body(core);

        int address = body.IndexOf(
            "memcpy(candidate_stun2->addr, candidate_stun->addr, sizeof(candidate_stun->addr));",
            StringComparison.Ordinal);
        if (address < 0)
            return false;

        int steps = body.IndexOf(
            "port_check += session->stun_allocation_increment;", address, StringComparison.Ordinal);
        int writes = body.IndexOf("candidate_stun2->port = port_check;", address, StringComparison.Ordinal);

        return steps > address && writes > steps;
    }

    /// <summary>
    /// Whether the array is still grown before the slots that outrun the first allocation are
    /// written - it is, which is why this is checked rather than claimed.
    /// </summary>
    public static bool TheArrayIsStillGrownFirst(string core)
    {
        string body = Body(core);

        int grown = body.IndexOf(
            $"realloc(msg.conn_request->candidates, sizeof(Candidate) * {OfferLayout.GrownSlots});",
            StringComparison.Ordinal);
        int written = body.IndexOf(
            $"memcpy(&msg.conn_request->candidates[{OfferLayout.EightGuesses}],", StringComparison.Ordinal);

        return grown >= 0 && written > grown;
    }

    /// <summary>holepunch_session_create_offer's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.LastIndexOf(
            "CHIAKI_EXPORT ChiakiErrorCode holepunch_session_create_offer(Session *session)",
            StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic ChiakiErrorCode send_offer(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
