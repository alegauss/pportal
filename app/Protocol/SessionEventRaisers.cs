using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which file in the C raises an event.</summary>
public enum EventRaiser
{
    /// <summary>streamconnection.c, which is eight of the frame path's nine.</summary>
    StreamConnection,

    /// <summary>videoreceiver.c, which is the ninth.</summary>
    VideoReceiver,

    /// <summary>ctrl.c, which raises the three keyboard events and nothing else.</summary>
    Ctrl,

    /// <summary>session.c, which raises four.</summary>
    Session,

    /// <summary>Nothing in lib/src assigns it. There is exactly one of these.</summary>
    Nobody,
}

/// <summary>One member of ChiakiEventType, with what raises it and what the port does about it.</summary>
/// <param name="Event">The member, as the managed mirror names it.</param>
/// <param name="Raiser">The C file that assigns this type, or Nobody.</param>
/// <param name="Subsystem">
/// The piece of work it belongs to, which is the question PP712 taught this census to ask: seven
/// owed members read as one job until somebody grouped them.
/// </param>
/// <param name="Managed">What answers it on the managed side, or null where nothing does.</param>
/// <param name="Why">What the counterpart is, or what its absence costs.</param>
public readonly record struct RaisedEvent(
    ChiakiEventType Event, EventRaiser Raiser, string Subsystem, Counterpart? Managed, string Why);

/// <summary>
/// PP722: the eight events outside the frame path, counted before any of them is ported.
///
/// PP719 wrote the nine the frame path raises and left the other eight named rather than answered.
/// This is the count PP712's lesson asks for first: seven owed members of the run's host read as
/// four subsystems until somebody asked which, and the same question here says the eight are FOUR
/// pieces of work, two of which are already done.
///
/// THE FOUR. The three keyboard events are one screen, and ctrl.c raises no others. The pin request
/// and the quit are one pair and the port already consumes both, through the front door's translate.
/// The auto-regist callback raises the remaining two. And CHIAKI_EVENT_HOLEPUNCH is raised by
/// nothing at all.
///
/// THE REGIST PAIR IS MUTUALLY EXCLUSIVE, which is the sort of thing a census finds and a port
/// misses. Both sit in the same regist callback under FINISHED_SUCCESS: the first fires under
/// <c>if(session->auto_regist)</c> and the second under <c>if(!ps5 &amp;&amp; !auto_regist)</c>. So
/// exactly one can fire, and on a PS5 session without auto-regist NEITHER does - a handler written
/// to expect a nickname after a successful registration would wait forever on the console this port
/// is for.
///
/// THE EIGHTH IS RAISED BY NOTHING AND ANSWERED ANYWAY. PunchProgress models the event, because
/// upstream's holepunch raised it and PP33 removed that file; gui/src/streamsession.cpp still
/// switches on it. The member stays because deleting it renumbers every value after it, which
/// NativeEnumMirrors is the check for.
/// </summary>
public static class SessionEventRaisers
{
    /// <summary>Where the enum is.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\session.h";

    /// <summary>The C files that raise anything, in the order this census names them.</summary>
    public static IReadOnlyList<string> RaiserRelativePaths { get; } =
    [
        @"lib\src\streamconnection.c", @"lib\src\videoreceiver.c", @"lib\src\ctrl.c", @"lib\src\session.c",
    ];

    /// <summary>The frame path's, which PP719 already answers and this census only accounts for.</summary>
    public const string FramePath = "the frame path";

    /// <summary>Every member of ChiakiEventType, in the header's order.</summary>
    public static IReadOnlyList<RaisedEvent> All { get; } =
    [
        new(
            ChiakiEventType.Connected, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendConnected)),
            "PP719's, and the one the run makes with the state mutex released."),
        new(
            ChiakiEventType.LoginPinRequest, EventRaiser.Session, "the front door's pin",
            new(CounterpartAssembly.AppSession, nameof(NativeConsoleSessionStarter), nameof(NativeConsoleSessionStarter.Start)),
            "Already consumed: the front door translates it to a state that asks for a pin."),
        new(
            ChiakiEventType.Holepunch, EventRaiser.Nobody, "nothing raises it",
            new(CounterpartAssembly.App, nameof(PunchProgress), nameof(PunchProgress.StateFor)),
            "Modelled and unraisable: PP33 removed the file that raised it, and the Qt client still switches on it."),
        new(
            ChiakiEventType.Regist, EventRaiser.Session, "the auto-regist callback",
            null,
            "Owed. Fires only under auto_regist, and carries the whole ChiakiRegisteredHost."),
        new(
            ChiakiEventType.NicknameReceived, EventRaiser.Session, "the auto-regist callback",
            null,
            "Owed, and the other half of that pair: fires only where the console is not a PS5 and auto_regist is off."),
        new(
            ChiakiEventType.KeyboardOpen, EventRaiser.Ctrl, "the on-screen keyboard",
            new(CounterpartAssembly.App, nameof(CtrlKeyboardArrivals), nameof(CtrlKeyboardArrivals.ReceiveOpen)),
            "The message is parsed; what is owed is a raiser, not a reader."),
        new(
            ChiakiEventType.KeyboardTextChange, EventRaiser.Ctrl, "the on-screen keyboard",
            new(CounterpartAssembly.App, nameof(CtrlKeyboardArrivals), nameof(CtrlKeyboardArrivals.ReceiveTextChange)),
            "The same, and PP531's indistinguishable-from-a-close case is about this one."),
        new(
            ChiakiEventType.KeyboardRemoteClose, EventRaiser.Ctrl, "the on-screen keyboard",
            new(CounterpartAssembly.App, nameof(CtrlKeyboardArrivals), nameof(CtrlKeyboardArrivals.ReceiveRemoteClose)),
            "The same."),
        new(
            ChiakiEventType.Rumble, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.Rumble)),
            "PP719's, with the three-byte floor the C logs and drops under."),
        new(
            ChiakiEventType.Quit, EventRaiser.Session, "the front door's pin",
            new(CounterpartAssembly.AppSession, nameof(NativeConsoleSessionStarter), nameof(NativeConsoleSessionStarter.Start)),
            "Already consumed: the front door translates it to an ended state with the quit sentence."),
        new(
            ChiakiEventType.TriggerEffects, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.TriggerEffects)),
            "PP719's, with the 0x19 floor and the five bytes nobody reads."),
        new(
            ChiakiEventType.MotionReset, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendPadInfo)),
            "PP719's, one of the pad info five."),
        new(
            ChiakiEventType.LedColor, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendPadInfo)),
            "The same."),
        new(
            ChiakiEventType.PlayerIndex, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendPadInfo)),
            "The same."),
        new(
            ChiakiEventType.HapticIntensity, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendPadInfo)),
            "The same."),
        new(
            ChiakiEventType.TriggerIntensity, EventRaiser.StreamConnection, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendPadInfo)),
            "The same."),
        new(
            ChiakiEventType.VideoFecFailure, EventRaiser.VideoReceiver, FramePath,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.VideoFecFailure)),
            "PP719's ninth, and the only one videoreceiver.c raises."),
    ];

    /// <summary>The ones outside the frame path, which is what this census was filed about.</summary>
    public static IReadOnlyList<RaisedEvent> Outside { get; } =
        [.. All.Where(one => one.Subsystem != FramePath)];

    /// <summary>
    /// The subsystems those belong to, which is what a plan is made from.
    ///
    /// Four, and two of them are already answered. PP712's count was objects rather than members,
    /// and so is this: the three keyboard events are one screen and the regist pair is one callback.
    /// </summary>
    public static IReadOnlyList<string> OutsideSubsystems { get; } =
        [.. Outside.Select(one => one.Subsystem).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The members with nothing managed answering them at all.</summary>
    public static IReadOnlyList<ChiakiEventType> Owed { get; } =
        [.. All.Where(one => one.Managed is null).Select(one => one.Event)];

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>One raiser's file, or null outside a checkout.</summary>
    public static string? Locate(string relativePath) => SanitizerSource.LocateRelative(relativePath);

    /// <summary>
    /// Every event type a C file assigns.
    ///
    /// PP719's sweep, not a second one - and widening its prefix by a word is what let this census
    /// check its own Raiser column, because two of session.c's four name their local something
    /// other than `event`.
    /// </summary>
    public static IReadOnlyList<string> RaisedIn(string cSource)
        => ManagedSessionEventsSource.EventsRaisedIn(cSource);

    /// <summary>The C's name for a managed member, which is the join the census rests on.</summary>
    public static string CNameOf(ChiakiEventType member)
    {
        string name = member.ToString();
        var built = new System.Text.StringBuilder("CHIAKI_EVENT_");

        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                built.Append('_');

            built.Append(char.ToUpperInvariant(name[i]));
        }

        return built.ToString();
    }
}
