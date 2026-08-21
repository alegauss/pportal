using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: how far a hole-punching session has got. A MASK, not a position - see
/// <see cref="HolepunchSessionState"/>.
/// </summary>
[Flags]
public enum SessionStateFlags
{
    /// <summary>Nothing has happened yet.</summary>
    None = 0,

    /// <summary>Declared, never entered.</summary>
    Init = 1 << 0,

    /// <summary>The push socket is up.</summary>
    WsOpen = 1 << 1,

    /// <summary>PSN has a session for us.</summary>
    Created = 1 << 2,

    /// <summary>Declared, never entered - and the "already started" guard tests it.</summary>
    Started = 1 << 3,

    /// <summary>This end is a member.</summary>
    ClientJoined = 1 << 4,

    /// <summary>The start data went out.</summary>
    DataSent = 1 << 5,

    /// <summary>And the console is a member too.</summary>
    ConsoleJoined = 1 << 6,

    /// <summary>The sixteen bytes of PP192 arrived.</summary>
    CustomData1Received = 1 << 7,

    /// <summary>An offer for the control port.</summary>
    CtrlOfferReceived = 1 << 8,

    /// <summary>Declared, never entered.</summary>
    CtrlOfferSent = 1 << 9,

    /// <summary>Declared, never entered.</summary>
    CtrlConsoleAccepted = 1 << 10,

    /// <summary>Declared, never entered.</summary>
    CtrlClientAccepted = 1 << 11,

    /// <summary>The control port is up.</summary>
    CtrlEstablished = 1 << 12,

    /// <summary>An offer for the data port.</summary>
    DataOfferReceived = 1 << 13,

    /// <summary>Declared, never entered.</summary>
    DataOfferSent = 1 << 14,

    /// <summary>Declared, never entered.</summary>
    DataConsoleAccepted = 1 << 15,

    /// <summary>Declared, never entered.</summary>
    DataClientAccepted = 1 << 16,

    /// <summary>The data port is up.</summary>
    DataEstablished = 1 << 17,

    /// <summary>And the session is gone.</summary>
    Deleted = 1 << 18,
}

/// <summary>
/// PP33: the session's progress, which is a bitmask that only ever gains bits.
///
/// IT IS A HISTORY, NOT A POSITION. Every transition is <c>state |= …</c> and there is not one
/// <c>&amp;= ~</c> anywhere in the file, so "the session is in state X" always means "the session
/// has at some point reached X". Nothing is ever unmade - not by a failure, not by a timeout, not by
/// the console leaving. A port that modelled this as a current-state enum would have to invent an
/// answer for every question the mask answers with two bits at once, and would get the auto-ACK
/// window below wrong in both directions.
///
/// EIGHT OF THE NINETEEN STATES ARE NEVER ENTERED. Init, Started, and the six "sent" and "accepted"
/// flags for the two ports are declared, are read, and are never once set. Every one of the eight
/// has at least one test against it, so eight branches in this file cannot be taken.
///
/// SEVEN OF THOSE READS ARE IN A LOG PRINTER, and are harmless: the log's vocabulary is simply
/// wider than the machine's, and the labels for those eight never print. THE EIGHTH IS NOT. The
/// guard at the top of session_start refuses a session that is already started by testing Started -
/// which nothing sets - so starting a session twice is not prevented at all. The check reads like
/// protection and is decoration.
///
/// THE AUTO-ACK WINDOW IS ASYMMETRIC BETWEEN THE TWO PORTS. Offers are acknowledged automatically
/// when the control offer has arrived and the control port is NOT yet established, or when the data
/// offer has arrived at all - so the control clause closes when the port comes up and the data
/// clause never closes. That is deliberate: auto-ACK is on exactly when nothing is explicitly
/// waiting for an offer, and after the data offer nothing ever is again.
/// </summary>
public sealed class HolepunchSessionState
{
    /// <summary>The states nothing ever sets.</summary>
    public static IReadOnlyList<SessionStateFlags> NeverEntered { get; } =
    [
        SessionStateFlags.Init,
        SessionStateFlags.Started,
        SessionStateFlags.CtrlOfferSent,
        SessionStateFlags.CtrlConsoleAccepted,
        SessionStateFlags.CtrlClientAccepted,
        SessionStateFlags.DataOfferSent,
        SessionStateFlags.DataConsoleAccepted,
        SessionStateFlags.DataClientAccepted,
    ];

    /// <summary>How much has happened so far.</summary>
    public SessionStateFlags Flags { get; private set; }

    /// <summary>Record that something happened. There is no way to unrecord it.</summary>
    public void Enter(SessionStateFlags flag) => Flags |= flag;

    /// <summary>Whether the session has ever reached that point.</summary>
    public bool Has(SessionStateFlags flag) => (Flags & flag) == flag;

    /// <summary>Whether creating the session is finished: PSN made one and this end joined it.</summary>
    public bool CreationFinished
        => Has(SessionStateFlags.Created) && Has(SessionStateFlags.ClientJoined);

    /// <summary>Whether starting is finished: the console joined and its sixteen bytes arrived.</summary>
    public bool StartFinished
        => Has(SessionStateFlags.ConsoleJoined) && Has(SessionStateFlags.CustomData1Received);

    /// <summary>
    /// Whether an offer arriving now is acknowledged automatically - which is to say, whether
    /// nothing is explicitly waiting for one.
    /// </summary>
    public bool ShouldAckOffers
        => (Has(SessionStateFlags.CtrlOfferReceived) && !Has(SessionStateFlags.CtrlEstablished))
            || Has(SessionStateFlags.DataOfferReceived);

    /// <summary>
    /// Whether starting would be refused as already started - which it never is, because nothing
    /// sets that flag. Kept as its own name so the dead guard is visible rather than absent.
    /// </summary>
    public bool WouldRefuseAsAlreadyStarted => Has(SessionStateFlags.Started);
}

/// <summary>
/// PP33: the state machine's rules where the Qt core states them.
/// </summary>
public static class SessionStateSource
{
    /// <summary>Where the states live.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The core's spelling of a state.</summary>
    public static string NameOf(SessionStateFlags flag) => flag switch
    {
        SessionStateFlags.Init => "SESSION_STATE_INIT",
        SessionStateFlags.WsOpen => "SESSION_STATE_WS_OPEN",
        SessionStateFlags.Created => "SESSION_STATE_CREATED",
        SessionStateFlags.Started => "SESSION_STATE_STARTED",
        SessionStateFlags.ClientJoined => "SESSION_STATE_CLIENT_JOINED",
        SessionStateFlags.DataSent => "SESSION_STATE_DATA_SENT",
        SessionStateFlags.ConsoleJoined => "SESSION_STATE_CONSOLE_JOINED",
        SessionStateFlags.CustomData1Received => "SESSION_STATE_CUSTOMDATA1_RECEIVED",
        SessionStateFlags.CtrlOfferReceived => "SESSION_STATE_CTRL_OFFER_RECEIVED",
        SessionStateFlags.CtrlOfferSent => "SESSION_STATE_CTRL_OFFER_SENT",
        SessionStateFlags.CtrlConsoleAccepted => "SESSION_STATE_CTRL_CONSOLE_ACCEPTED",
        SessionStateFlags.CtrlClientAccepted => "SESSION_STATE_CTRL_CLIENT_ACCEPTED",
        SessionStateFlags.CtrlEstablished => "SESSION_STATE_CTRL_ESTABLISHED",
        SessionStateFlags.DataOfferReceived => "SESSION_STATE_DATA_OFFER_RECEIVED",
        SessionStateFlags.DataOfferSent => "SESSION_STATE_DATA_OFFER_SENT",
        SessionStateFlags.DataConsoleAccepted => "SESSION_STATE_DATA_CONSOLE_ACCEPTED",
        SessionStateFlags.DataClientAccepted => "SESSION_STATE_DATA_CLIENT_ACCEPTED",
        SessionStateFlags.DataEstablished => "SESSION_STATE_DATA_ESTABLISHED",
        SessionStateFlags.Deleted => "SESSION_STATE_DELETED",
        _ => "",
    };

    /// <summary>How many times a state is set, and how many times it is tested.</summary>
    public static (int Set, int Read) CountsFor(string core, SessionStateFlags flag)
    {
        ArgumentNullException.ThrowIfNull(core);

        string name = NameOf(flag);
        return (Count(core, $"state |= {name};"), Count(core, $"state & {name}"));
    }

    /// <summary>Whether the mask is still only ever added to.</summary>
    public static bool NothingIsEverUnset(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return !core.Contains("state &= ~", StringComparison.Ordinal)
            && !core.Contains("state &=~", StringComparison.Ordinal);
    }

    /// <summary>Whether the "already started" guard is still testing a flag nothing sets.</summary>
    public static bool TheStartedGuardIsStillDead(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if (session->state & SESSION_STATE_STARTED)", StringComparison.Ordinal)
            && core.Contains("Holepunch session already started", StringComparison.Ordinal)
            && Count(core, "state |= SESSION_STATE_STARTED;") == 0;
    }

    /// <summary>Whether the auto-ACK window is still asymmetric between the two ports.</summary>
    public static bool TheAckWindowIsStillAsymmetric(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("(session->state & SESSION_STATE_CTRL_OFFER_RECEIVED", StringComparison.Ordinal)
            && core.Contains("&& !(session->state & SESSION_STATE_CTRL_ESTABLISHED))", StringComparison.Ordinal)
            && core.Contains("|| session->state & SESSION_STATE_DATA_OFFER_RECEIVED;", StringComparison.Ordinal);
    }

    /// <summary>Whether the two finish conditions still each want exactly two bits.</summary>
    public static bool TheFinishConditionsStillWantTwoBitsEach(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("finished = (session->state & SESSION_STATE_CREATED) &&", StringComparison.Ordinal)
            && core.Contains("(session->state & SESSION_STATE_CLIENT_JOINED);", StringComparison.Ordinal)
            && core.Contains("finished = (session->state & SESSION_STATE_CONSOLE_JOINED) &&", StringComparison.Ordinal)
            && core.Contains("(session->state & SESSION_STATE_CUSTOMDATA1_RECEIVED);", StringComparison.Ordinal);
    }

    private static int Count(string core, string needle)
    {
        int count = 0;
        int at = 0;

        while ((at = core.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at++;
        }

        return count;
    }
}
