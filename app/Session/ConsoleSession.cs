using System.Net;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>Where a session got to, as the front door needs to say it.</summary>
public enum ConsoleSessionState
{
    /// <summary>Asked for, and the console has not answered yet.</summary>
    Starting,

    /// <summary>The control conversation finished. This is what connecting means.</summary>
    Connected,

    /// <summary>
    /// PP627: the console is asking for a login PIN, and will wait for one indefinitely.
    ///
    /// The only state that needs something back. session.c waits on it with `UINT64_MAX` - no
    /// timeout at all - because a person typing is not something a network timeout should
    /// interrupt, so a session in this state goes nowhere until somebody answers or ctrl fails.
    /// </summary>
    PinWanted,

    /// <summary>Over, for a reason - which is <see cref="ConsoleSessionEvent.Sentence"/>.</summary>
    Ended,
}

/// <summary>One thing that happened to a session, in words a screen can print.</summary>
/// <param name="State">Where it got to.</param>
/// <param name="Sentence">What to say, or null where the state says it all.</param>
public readonly record struct ConsoleSessionEvent(ConsoleSessionState State, string? Sentence);

/// <summary>
/// PP625: how a quit becomes a sentence, which is qmlbackend.cpp's dialog and not an invention.
///
/// The two facts that make this more than a ToString, both of them already written down in
/// <see cref="ChiakiSessionEvent"/> and neither of them acted on until now:
///
///   the reason string libchiaki carries is filled ONLY from a disconnect the console itself sent,
///   so it is null on every failure that never reached one - including the commonest of them all,
///   a console that is switched off;
///
///   and a quit is not always a failure. `chiaki_quit_reason_is_error` in session.h is false for
///   STOPPED and for the console shutting down remotely, and a port that showed an error for those
///   would tell somebody their own Disconnect went wrong.
/// </summary>
public static class QuitSentence
{
    /// <summary>What the client says a session ending is, when it is not an error.</summary>
    public const string Ended = "The session ended.";

    /// <summary>
    /// Whether this reason is a failure - `chiaki_quit_reason_is_error`, which is a static inline
    /// in session.h and therefore has no symbol the shim could wrap.
    ///
    /// Restated here rather than reached through the seam, and the two are held together by an
    /// assertion that reads the header: a `static inline` is not exported, so this is the one shape
    /// of C rule the port has to carry a copy of.
    /// </summary>
    public static bool IsError(ChiakiQuitReason reason)
        => reason != ChiakiQuitReason.Stopped
            && reason != ChiakiQuitReason.StreamConnectionRemoteShutdown;

    /// <summary>
    /// The sentence, composed the way the Qt client's own dialog composes it.
    ///
    /// One line rather than the client's three, because this goes on a status line and not into a
    /// message box. What is kept is the shape: the reason libchiaki names, and the console's own
    /// words after it where there are any.
    /// </summary>
    public static string For(ChiakiQuitReason reason, string? fromConsole)
    {
        if (!IsError(reason))
            return Ended;

        string named = ChiakiSession.QuitReasonString((int)reason) ?? reason.ToString();

        return string.IsNullOrWhiteSpace(fromConsole)
            ? $"The session has quit: {named}"
            : $"The session has quit: {named} - \"{fromConsole}\"";
    }
}

/// <summary>
/// PP627: a session that is being held, and the one thing that can be said back to it.
///
/// An interface rather than <see cref="IDisposable"/> alone, because the PIN is the only answer a
/// session takes and it has to reach the same object the list is holding. Ending it is still
/// <see cref="IDisposable.Dispose"/>.
/// </summary>
public interface IHeldSession : IDisposable
{
    /// <summary>Answers a <see cref="ConsoleSessionState.PinWanted"/>, and says what libchiaki said.</summary>
    ChiakiError AnswerPin(ReadOnlySpan<byte> pin);
}

/// <summary>What starting a session produced: a live one, or the error that stopped it.</summary>
/// <param name="Error">What libchiaki said.</param>
/// <param name="Session">The handle that keeps it alive, disposed to end it.</param>
public readonly record struct ConsoleSessionStart(ChiakiError Error, IHeldSession? Session)
{
    /// <summary>Whether there is a session to hold.</summary>
    public bool Running => Error == ChiakiError.Success && Session is not null;
}

/// <summary>
/// Where a prepared request becomes a session that OUTLIVES the call.
///
/// PP600's version answered with an error code and released the session on the way out, so a
/// connect that succeeded ended in the same instant. That was deliberate - there was nowhere to
/// hand a running session to - and it made "Connecting..." a sentence about a call rather than
/// about a console.
///
/// What crosses the seam is still only the request, plus somewhere to report to. The reports arrive
/// on libchiaki's session thread, which is why every caller here marshals.
/// </summary>
public interface IConsoleSessionStarter
{
    /// <summary>Starts a session and hands back the handle that holds it.</summary>
    ConsoleSessionStart Start(ConnectRequest request, Action<ConsoleSessionEvent> report);
}

/// <summary>
/// The real one: the capture's four calls, with the session kept.
///
/// The connect info is disposed as soon as the session exists - libchiaki copies what it needs in
/// `chiaki_session_init`, which is what lets the capture use a `using` on it - and the session is
/// stopped and disposed when this handle is.
/// </summary>
public sealed class NativeConsoleSessionStarter : IConsoleSessionStarter
{
    /// <inheritdoc />
    public ConsoleSessionStart Start(ConnectRequest request, Action<ConsoleSessionEvent> report)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);

        ChiakiSession.LibInit();

        // PP801: the phase's takion needs an ADDRESS, and a name is not one.
        //
        // Refused here rather than started and left to quit: since PP783 the C does not run the
        // stream, so a session with no phase reaches it and ends with UNINITIALIZED after the whole
        // ctrl handshake. A refusal at the door names the reason; the other way spends twenty
        // seconds and reports "Ended". Every path this tree exercises passes a discovered address.
        if (!IPAddress.TryParse(request.Host, out IPAddress? console))
            return new(ChiakiError.InvalidData, null);

        ChiakiSession? session;
        ChiakiError created;

        using (var info = new ChiakiConnectInfo { Host = request.Host, Ps5 = request.Ps5 })
        {
            info.SetRegistKey(request.RegistKey);
            info.SetMorning(request.Morning);
            info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);
            info.SetFlags(autoDowngrade: true, keyboard: false, dualSense: false, idrOnFecFailure: false);

            session = ChiakiSession.TryCreate(info, null, out created);
        }

        if (session is null)
            return new(created, null);

        // PP801: AND THE FRONT DOOR INSTALLS ONE TOO, which it never did. PP783 flipped the run and
        // wired the one path that shows a picture; this is the one a person clicks, and it reached
        // the stream phase and quit.
        //
        // NO DECODER AND NO WINDOW, which is what this door has. The frame is taken and reported
        // taken rather than refused: the receiver asks the console for a keyframe on a false, so a
        // door with nowhere to put a picture would ask for an IDR sixty times a second.
        //
        // Held by the handle, because the phase must outlive this call exactly as the session does -
        // a phase disposed on the way out cancels its own run.
        var baseline = new SessionBaseline();
        var phase = new ManagedStreamPhase(session, console, TakenAndDropped, baseline);

        // PP784: AND THE MEASUREMENT, for the same reason one line later than PP801's.
        //
        // session.c measured with the C's own senkusha for anything with no phase installed, so this
        // door got real numbers without asking. That arm is gone - a live link said the two agree and
        // keeping both left nothing able to say which had run - and what a door without a phase gets
        // now is 1454/1454/1000. It STREAMS on those, which is why this is worth a line: the failure
        // is a launch spec built from numbers no console answered, on a session that looks healthy.
        var senkusha = new ManagedSenkushaPhase(session, console);

        // Armed BEFORE the start, for the capture's reason: a console that refuses immediately
        // answers on the session thread while this one is still returning.
        session.SetEventHandler(e => report(Translate(e)));

        phase.InstallOn();
        senkusha.InstallOn();

        ChiakiError started = session.Start();
        if (started != ChiakiError.Success)
        {
            senkusha.Dispose();
            phase.Dispose();
            baseline.Dispose();
            session.Dispose();
            return new(started, null);
        }

        return new(ChiakiError.Success, new Handle(session, phase, senkusha, baseline));
    }

    /// <summary>
    /// The events the front door has words for. Everything else is dropped rather than reported as
    /// a state it does not have - a screen that said "Rumble" would be reading the enum aloud.
    /// </summary>
    private static ConsoleSessionEvent Translate(ChiakiSessionEvent e) => e.Type switch
    {
        ChiakiEventType.Connected => new(ConsoleSessionState.Connected, null),
        ChiakiEventType.LoginPinRequest => new(ConsoleSessionState.PinWanted, null),
        ChiakiEventType.Quit => new(
            ConsoleSessionState.Ended, QuitSentence.For(e.QuitReason, e.QuitReasonString)),
        _ => new(ConsoleSessionState.Starting, null),
    };

    /// <summary>
    /// PP801: video this door has nowhere to put, taken rather than refused.
    ///
    /// TRUE and not false, which is the difference between dropping a frame and asking for another.
    /// The session's contract says a false reports the frame corrupt so a new keyframe is fetched,
    /// so a handler with no decoder that answered false would ask the console for an IDR per frame
    /// for the whole session.
    /// </summary>
    private static bool TakenAndDropped(ReadOnlySpan<byte> frame, int framesLost, bool recovered)
        => true;

    /// <summary>
    /// The handle. Stops before it disposes, because a session thread still running when the
    /// object goes is the one shape of teardown that takes the process with it.
    ///
    /// PP801: it holds the PHASE as well, and the order is the session's stop first. Stopping pokes
    /// the phase through the C's callback, so a phase disposed before the session would be cancelled
    /// twice - and the session's own stop would then reach a handover that had gone.
    ///
    /// PP784: and the SENKUSHA phase, on the same rule. Its run is over long before a session ends,
    /// but the session's stop still pokes it through a second callback, so it outlives the stop for
    /// the reason the stream's does rather than because it is still measuring.
    /// </summary>
    private sealed class Handle(
        ChiakiSession session,
        ManagedStreamPhase phase,
        ManagedSenkushaPhase senkusha,
        SessionBaseline baseline)
        : IHeldSession
    {
        private ChiakiSession? held = session;

        /// <inheritdoc />
        public ChiakiError AnswerPin(ReadOnlySpan<byte> pin)
            => held is { } running ? running.SetLoginPin(pin) : ChiakiError.InvalidData;

        public void Dispose()
        {
            ChiakiSession? going = Interlocked.Exchange(ref held, null);
            if (going is null)
                return;

            going.Stop();
            going.Join();
            going.Dispose();

            senkusha.Dispose();
            phase.Dispose();
            baseline.Dispose();
        }
    }
}

