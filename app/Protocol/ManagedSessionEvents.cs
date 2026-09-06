using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>ChiakiRumbleEvent: three bytes, the first of which nobody has named.</summary>
/// <param name="Unknown">buf[0]. The C calls it unknown and passes it on regardless.</param>
/// <param name="Left">The low-frequency motor.</param>
/// <param name="Right">And the high-frequency one.</param>
public readonly record struct RumbleState(byte Unknown, byte Left, byte Right);

/// <summary>
/// ChiakiTriggerEffectsEvent: two effect types and two ten-byte blobs.
///
/// The blobs are compared by CONTENT and not by reference, for <see cref="PadLed"/>'s reason one
/// payload over: the generated equality of a record holding an array is the array's identity, so two
/// events carrying the same effect would report as different and a test asserting on one would be
/// asserting that the same allocation came back.
///
/// An empty side reads as ten zero bytes, which is what <c>ChiakiEvent event = { 0 }</c> leaves in
/// the union when the raiser is not this one.
/// </summary>
/// <param name="TypeLeft">buf[1]. The effect the left trigger is to run.</param>
/// <param name="TypeRight">buf[2].</param>
/// <param name="Left">buf[5..15], the left trigger's parameters.</param>
/// <param name="Right">buf[15..25].</param>
public readonly record struct TriggerEffectsState(
    byte TypeLeft, byte TypeRight, ReadOnlyMemory<byte> Left, ReadOnlyMemory<byte> Right)
{
    /// <summary>How many bytes one side's effect data is, as the C's array is declared.</summary>
    public const int SideBytes = 10;

    private static readonly byte[] Zeroed = new byte[SideBytes];

    /// <summary>One side's bytes, with the union's own zero where nothing filled it.</summary>
    public static ReadOnlySpan<byte> SideOf(ReadOnlyMemory<byte> side)
        => side.IsEmpty ? Zeroed : side.Span;

    /// <summary>The left side's ten bytes.</summary>
    public ReadOnlySpan<byte> LeftBytes => SideOf(Left);

    /// <summary>And the right's.</summary>
    public ReadOnlySpan<byte> RightBytes => SideOf(Right);

    /// <inheritdoc/>
    public bool Equals(TriggerEffectsState other)
        => TypeLeft == other.TypeLeft
            && TypeRight == other.TypeRight
            && LeftBytes.SequenceEqual(other.LeftBytes)
            && RightBytes.SequenceEqual(other.RightBytes);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(TypeLeft);
        hash.Add(TypeRight);
        hash.AddBytes(LeftBytes);
        hash.AddBytes(RightBytes);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One ChiakiEvent: a type, and every arm of the union the C declares beside it.
///
/// FLAT, BECAUSE THE ZERO IS THE BEHAVIOUR. Each raiser writes <c>ChiakiEvent event = { 0 }</c> and
/// then fills its own arm, so a motion reset carries a zero player index rather than the last one
/// the console sent. A payload held apart - one object per event kind - could not express that, and
/// the difference is visible to anything that reads a field the raiser did not set.
///
/// Only the arms the frame path fills are here. The keyboard, the quit reason and the registered
/// host belong to ctrl.c and session.c, which <see cref="ManagedSessionEvents"/> does not raise for.
/// </summary>
/// <param name="Type">Which event it is, which is the only field every raiser sets.</param>
/// <param name="Rumble">CHIAKI_EVENT_RUMBLE's three bytes.</param>
/// <param name="TriggerEffects">CHIAKI_EVENT_TRIGGER_EFFECTS' two types and two blobs.</param>
/// <param name="Intensity">The haptic or the trigger intensity, which share one union member.</param>
/// <param name="Led">CHIAKI_EVENT_LED_COLOR's three bytes.</param>
/// <param name="PlayerIndex">CHIAKI_EVENT_PLAYER_INDEX's byte.</param>
/// <param name="FecFrameIndex">CHIAKI_EVENT_VIDEO_FEC_FAILURE's frame.</param>
/// <param name="FecIdrRequestSent">And whether a keyframe had already been asked for.</param>
public readonly record struct SessionEvent(
    ChiakiEventType Type,
    RumbleState Rumble = default,
    TriggerEffectsState TriggerEffects = default,
    DualSenseEffectIntensity Intensity = default,
    PadLed Led = default,
    byte PlayerIndex = 0,
    int FecFrameIndex = 0,
    bool FecIdrRequestSent = false);

/// <summary>
/// Where a raised event goes: <c>session->event_cb</c>, and nothing else in the C.
/// </summary>
public interface ISessionEventSink
{
    /// <summary>Take one event. The C's callback returns nothing and cannot refuse.</summary>
    void Send(in SessionEvent raised);
}

/// <summary>
/// PP719, under PP707: chiaki_session_send_event, and the nine events the frame path raises.
///
/// PP712's census owes the run's host a SendConnected and the row's reason is the finding:
/// <see cref="StreamRun"/> READS CHIAKI_EVENT_CONNECTED off the C session and nothing here sends
/// one. So the owed piece is not a call - it is the callback the C calls, which streamconnection.c
/// reaches eight times and videoreceiver.c once.
///
/// THE NULL CALLBACK IS THE BEHAVIOUR, not a guard against one. chiaki_session_send_event returns
/// where no callback is registered, which is what lets every raiser be unconditional: a session with
/// no application attached drops its events rather than failing. <see cref="Send"/> answers false
/// there and counts it, because "nobody was listening" is a different fact from "nothing happened".
///
/// THE TWO PARSES KEEP THE C'S SIZE GUARDS. Rumble is refused under three bytes and trigger effects
/// under <see cref="TriggerEffectsMinimum"/>, and both are logged and dropped rather than raised
/// short - which is why they answer null instead of an event with default bytes in it.
///
/// THE FIVE FROM PAD INFO ARE PP689'S, IN ITS ORDER. That task decided them after the switch so both
/// layouts share one sequence; <see cref="SendPadInfo"/> is what finally sends them, so the ordering
/// it asserted is now the ordering something does.
/// </summary>
public sealed class ManagedSessionEvents
{
    /// <summary>Under three bytes the rumble handler logs the size and raises nothing.</summary>
    public const int RumbleMinimum = 3;

    /// <summary>And under 0x19 the trigger effects handler does the same.</summary>
    public const int TriggerEffectsMinimum = 0x19;

    /// <summary>Where the left trigger's effect type sits. buf[0] is read by nobody.</summary>
    public const int TriggerTypeLeftOffset = 1;

    /// <summary>And the right's.</summary>
    public const int TriggerTypeRightOffset = 2;

    /// <summary>Where the left trigger's ten parameter bytes start - past buf[3] and buf[4].</summary>
    public const int TriggerLeftOffset = 5;

    /// <summary>And the right's, immediately after them.</summary>
    public const int TriggerRightOffset = TriggerLeftOffset + TriggerEffectsState.SideBytes;

    private ISessionEventSink? sink;

    /// <summary>How many events reached a sink.</summary>
    public int Sent { get; private set; }

    /// <summary>And how many were raised with none registered, which the C does silently.</summary>
    public int Unheard { get; private set; }

    /// <summary>Whether anything is listening. <c>session->event_cb</c>, as the C tests it.</summary>
    public bool IsHeard => sink is not null;

    /// <summary>
    /// The nine the frame path raises, in the order the two files declare them.
    ///
    /// Named rather than derived from the enum, because the enum has seventeen and eight of those
    /// belong to ctrl.c, session.c and the holepunch. This list is what PP696 deletes.
    /// </summary>
    public static IReadOnlyList<ChiakiEventType> RaisedByTheFramePath { get; } =
    [
        ChiakiEventType.Connected,
        ChiakiEventType.Rumble,
        ChiakiEventType.TriggerEffects,
        ChiakiEventType.MotionReset,
        ChiakiEventType.HapticIntensity,
        ChiakiEventType.TriggerIntensity,
        ChiakiEventType.LedColor,
        ChiakiEventType.PlayerIndex,
        ChiakiEventType.VideoFecFailure,
    ];

    /// <summary>chiaki_session_set_event_cb. Null detaches, which the C also allows.</summary>
    public void Listen(ISessionEventSink? listener) => sink = listener;

    /// <summary>
    /// chiaki_session_send_event: hand it over, or return having done nothing.
    /// </summary>
    /// <returns>Whether a callback was there to take it.</returns>
    public bool Send(in SessionEvent raised)
    {
        if (sink is null)
        {
            Unheard++;
            return false;
        }

        Sent++;
        sink.Send(in raised);

        return true;
    }

    /// <summary>
    /// IStreamRunHost.SendConnected: the one the run makes with the state mutex released.
    ///
    /// PP640's third ordering is about where this is called from and not about what it carries -
    /// the C unlocks, sends and locks again, because a handler may call back into the session.
    /// </summary>
    public bool SendConnected() => Send(Connected());

    /// <summary>
    /// The five a pad info message decided, in PP689's order and none of the ones it did not.
    /// </summary>
    /// <returns>How many went out, which is the reports' count where anything is listening.</returns>
    public int SendPadInfo(PadInfoReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading.Reports);

        int sent = 0;

        foreach (PadReportKind kind in reading.Reports)
        {
            if (Send(ForPad(kind, reading.State)))
                sent++;
        }

        return sent;
    }

    /// <summary>CHIAKI_EVENT_CONNECTED, which carries nothing but itself.</summary>
    public static SessionEvent Connected() => new(ChiakiEventType.Connected);

    /// <summary>
    /// The rumble, or null where the payload is short.
    ///
    /// Three bytes and no more: the C reads buf[0..2] whatever the length is above the guard.
    /// </summary>
    public static SessionEvent? Rumble(ReadOnlySpan<byte> payload)
        => payload.Length < RumbleMinimum
            ? null
            : new SessionEvent(
                ChiakiEventType.Rumble,
                Rumble: new RumbleState(payload[0], payload[1], payload[2]));

    /// <summary>
    /// The trigger effects, or null where the payload is short.
    ///
    /// Five of the twenty-five bytes are read by nobody - buf[0], buf[3] and buf[4] between the
    /// fields, and the guard is on the whole message rather than on what it takes from it.
    /// </summary>
    public static SessionEvent? TriggerEffects(ReadOnlySpan<byte> payload)
        => payload.Length < TriggerEffectsMinimum
            ? null
            : new SessionEvent(
                ChiakiEventType.TriggerEffects,
                TriggerEffects: new TriggerEffectsState(
                    payload[TriggerTypeLeftOffset],
                    payload[TriggerTypeRightOffset],
                    payload.Slice(TriggerLeftOffset, TriggerEffectsState.SideBytes).ToArray(),
                    payload.Slice(TriggerRightOffset, TriggerEffectsState.SideBytes).ToArray()));

    /// <summary>
    /// One of the pad info five, filled from the state the message left behind.
    ///
    /// The state AFTER, which is the C's: it writes the new value onto the stream connection and
    /// then reads it back into the event, so an event carrying what was held before would be a
    /// port right about the sequence and wrong about the number.
    /// </summary>
    public static SessionEvent ForPad(PadReportKind kind, PadState state) => kind switch
    {
        // The console asking rather than telling: no payload at all.
        PadReportKind.MotionReset => new SessionEvent(ChiakiEventType.MotionReset),

        PadReportKind.HapticIntensity => new SessionEvent(
            ChiakiEventType.HapticIntensity,
            Intensity: (DualSenseEffectIntensity)state.HapticIntensity),

        PadReportKind.TriggerIntensity => new SessionEvent(
            ChiakiEventType.TriggerIntensity,
            Intensity: (DualSenseEffectIntensity)state.TriggerIntensity),

        PadReportKind.LedColor => new SessionEvent(ChiakiEventType.LedColor, Led: state.Led),

        PadReportKind.PlayerIndex => new SessionEvent(
            ChiakiEventType.PlayerIndex, PlayerIndex: state.PlayerIndex),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not one of the pad info five"),
    };

    /// <summary>
    /// videoreceiver.c's one, whose two fields <see cref="IVideoReceiverOutbound.FecFailure"/>
    /// already carries.
    /// </summary>
    /// <param name="frameIndex">frame_index_cur, the frame that could not be recovered.</param>
    /// <param name="idrRequestSent">Whether a keyframe had already been asked for.</param>
    public static SessionEvent VideoFecFailure(int frameIndex, bool idrRequestSent)
        => new(
            ChiakiEventType.VideoFecFailure,
            FecFrameIndex: frameIndex,
            FecIdrRequestSent: idrRequestSent);
}

/// <summary>
/// PP719: the nine raisers and the null-callback return, read out of the C rather than trusted.
///
/// The events are the last thing streamconnection.c and videoreceiver.c do that had no managed
/// counterpart, so what this holds is the join PP696 will delete: which events those two files
/// raise, in what order, and whether the send still returns where nobody is listening.
/// </summary>
public static class ManagedSessionEventsSource
{
    /// <summary>Where eight of the nine are raised.</summary>
    public const string StreamRelativePath = StreamDispatchSource.RelativePath;

    /// <summary>And the ninth.</summary>
    public const string VideoRelativePath = @"lib\src\videoreceiver.c";

    /// <summary>Where the send itself is.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>
    /// What a raiser writes, which is how one is found.
    ///
    /// PP722 WIDENED IT BY ONE WORD. It read "event.type = " and every raiser in the two files this
    /// was written for names its local `event`, so it was right about all nine. session.c names two
    /// of its four `event_auto_regist` and `event_start`, and those the old prefix walked straight
    /// past - a sweep that reported two raisers in a file that has four. The local's name is not
    /// part of the C's shape, so the member access is where the pattern should start.
    /// </summary>
    public const string RaiserPrefix = ".type = CHIAKI_EVENT_";

    /// <summary>The prefix the C's own members carry, for the join to the managed enum.</summary>
    public const string EventPrefix = "CHIAKI_EVENT_";

    /// <summary>The send, whose whole body is the guard and the call.</summary>
    public const string SendSignature = "void chiaki_session_send_event(";

    /// <summary>One of the three files, or null outside a checkout.</summary>
    public static string? Locate(string relativePath) => SanitizerSource.LocateRelative(relativePath);

    /// <summary>
    /// Every event raised in a file, in the order the file raises them.
    ///
    /// Order rather than a set, because five of the nine are sent one after another from one
    /// handler and PP689's whole finding is that the sequence is the behaviour.
    /// </summary>
    public static IReadOnlyList<string> EventsRaisedIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        for (int at = source.IndexOf(RaiserPrefix, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(RaiserPrefix, at + RaiserPrefix.Length, StringComparison.Ordinal))
        {
            int from = at + RaiserPrefix.Length - EventPrefix.Length;
            int end = source.IndexOf(';', from);
            if (end < 0)
                break;

            found.Add(source[from..end].Trim());
        }

        return found;
    }

    /// <summary>The send's body, or null where it is gone.</summary>
    public static string? SendBody(string sessionSource)
        => CFunction.Body(sessionSource, SendSignature);

    /// <summary>
    /// Whether the send still returns before it calls, where no callback is registered.
    ///
    /// The whole of the C's body is that test and the call, and it is what lets every raiser above
    /// be written unconditionally. A port that required a sink would turn a session with no
    /// application attached into a failure on the first event rather than a quiet drop.
    /// </summary>
    public static bool TheSendStillReturnsWithNoCallback(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        int guard = sendBody.IndexOf("if(!session->event_cb)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int returned = sendBody.IndexOf("return;", guard, StringComparison.Ordinal);
        int called = sendBody.IndexOf("session->event_cb(", guard, StringComparison.Ordinal);

        return returned > guard && called > returned;
    }
}
