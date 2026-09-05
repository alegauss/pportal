using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// A controller state, as the diff needs to see it: no handle, no motion, no sticks.
///
/// The recorder is about what CHANGED between two states, and motion and sticks change constantly
/// without ever becoming a history event - they ride the feedback state instead. So this carries
/// only what the diff reads, which is also what makes two snapshots comparable by value.
/// </summary>
/// <param name="Buttons">The sixteen digital buttons as a bitmask.</param>
/// <param name="L2">The left trigger's level, 0 at rest.</param>
/// <param name="R2">The right trigger's.</param>
/// <param name="Touches">Both slots. An id below zero is a finger that is up.</param>
public readonly record struct PadSnapshot(
    ChiakiControllerButton Buttons,
    byte L2,
    byte R2,
    ChiakiControllerTouch Slot0,
    ChiakiControllerTouch Slot1)
{
    /// <summary>An empty slot, which is what chiaki_controller_state_set_idle leaves.</summary>
    public static ChiakiControllerTouch NoTouch => new(0, 0, -1);

    /// <summary>What a pad being held still reports, and where every session starts.</summary>
    public static PadSnapshot Idle => new(ChiakiControllerButton.None, 0, 0, NoTouch, NoTouch);

    /// <summary>Both slots in the order the C walks them, which is the order events come out in.</summary>
    public ChiakiControllerTouch Slot(int index) => index == 0 ? Slot0 : Slot1;

    /// <summary>Read the two slots off a live state, which is where a real snapshot comes from.</summary>
    public static PadSnapshot From(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        (byte l2, byte r2) = state.Triggers;
        return new PadSnapshot(state.Buttons, l2, r2, state.Touch(0), state.Touch(1));
    }
}

/// <summary>What a difference becomes: one history event, before it is serialised.</summary>
/// <param name="Kind">Touchpad or button, which picks the formatter.</param>
/// <param name="Down">Touchpad only: whether the finger is down.</param>
/// <param name="PointerId">Touchpad only: which finger.</param>
/// <param name="X">Touchpad only.</param>
/// <param name="Y">Touchpad only.</param>
/// <param name="Button">Button only: which one, analog triggers included.</param>
/// <param name="State">Button only: 0xff, 0, or a trigger's level.</param>
public readonly record struct HistoryEvent(
    HistoryEventKind Kind,
    bool Down,
    byte PointerId,
    ushort X,
    ushort Y,
    ChiakiControllerButton Button,
    byte State)
{
    /// <summary>A finger arriving, moving, or leaving.</summary>
    public static HistoryEvent Touch(bool down, byte pointerId, ushort x, ushort y)
        => new(HistoryEventKind.Touchpad, down, pointerId, x, y, ChiakiControllerButton.None, 0);

    /// <summary>A button, digital or analog.</summary>
    public static HistoryEvent Press(ChiakiControllerButton button, byte state)
        => new(HistoryEventKind.Button, false, 0, 0, 0, button, state);

    /// <summary>The bytes this event is, through PP676's formatters.</summary>
    public byte[] Serialise()
    {
        Span<byte> buf = stackalloc byte[FeedbackPayload.HistoryEventSizeMax];

        if (Kind == HistoryEventKind.Touchpad)
            return buf[..FeedbackPayload.TouchpadEvent(buf, Down, PointerId, X, Y)].ToArray();

        ChiakiError err = FeedbackPayload.ButtonEvent(buf, Button, State, out int written);
        return err == ChiakiError.Success
            ? buf[..written].ToArray()
            : throw new InvalidOperationException($"no history event for {Button}: {err}.");
    }
}

/// <summary>Which of the two formatters an event goes through.</summary>
public enum HistoryEventKind
{
    /// <summary>chiaki_feedback_history_event_set_touchpad, always five bytes.</summary>
    Touchpad,

    /// <summary>chiaki_feedback_history_event_set_button, two or three.</summary>
    Button,
}

/// <summary>
/// PP717: feedback_sender_record_history - what a controller change becomes on the wire.
///
/// PP676 ported the serialisers and PP712 recorded that nothing calls them. This is the caller: the
/// diff between the last state the console was told about and the one in hand, in the order the C
/// walks it - both touch slots, then the sixteen buttons from bit 0 upward, then L2, then R2.
///
/// THE TOUCH BRANCH IS AN ELSE, and that is the detail a port written from a description gets
/// wrong. A slot whose id changed from one valid touch to another emits the RELEASE of the old
/// finger and NOT the press of the new one: one finger lifting as another lands in the same slot is
/// a single event, and the arrival is reported on the next change instead.
///
/// AND THE TWO TRIGGERS CARRY THEIR LEVEL where the sixteen digital buttons carry 0xff or 0. A
/// trigger at rest and a trigger released are the same byte, so a port that sent 0xff for a
/// non-zero trigger would be wrong about how hard it was being held.
///
/// The function is static in feedbacksender.c, so there is no oracle to call it. What holds this to
/// the C is its own text, read by FeedbackRecorderSource the way PP669's censuses read theirs -
/// and every event produced here is serialised by the formatters that already answer to the C byte
/// for byte.
/// </summary>
public static class FeedbackHistoryRecorder
{
    /// <summary>CHIAKI_CONTROLLER_BUTTONS_COUNT: the digital buttons, bit 0 to bit 15.</summary>
    public const int ButtonsCount = 16;

    /// <summary>What a pressed digital button reports. The triggers do not use it.</summary>
    public const byte Pressed = 0xff;

    /// <summary>
    /// Every event the change from <paramref name="previous"/> to <paramref name="now"/> produces.
    ///
    /// Empty where nothing the history cares about moved - which is the common case, since sticks
    /// and motion change on almost every sample and produce nothing here.
    /// </summary>
    public static IReadOnlyList<HistoryEvent> Record(PadSnapshot previous, PadSnapshot now)
    {
        var events = new List<HistoryEvent>();

        for (int slot = 0; slot < ChiakiControllerState.TouchesMax; slot++)
        {
            ChiakiControllerTouch was = previous.Slot(slot);
            ChiakiControllerTouch is_ = now.Slot(slot);

            if (was.Id != is_.Id && was.Id >= 0)
            {
                // The OLD finger leaving, at the position it was last seen at.
                events.Add(HistoryEvent.Touch(false, (byte)was.Id, was.X, was.Y));
            }
            else if (is_.Id >= 0 && (was.Id != is_.Id || was.X != is_.X || was.Y != is_.Y))
            {
                events.Add(HistoryEvent.Touch(true, (byte)is_.Id, is_.X, is_.Y));
            }
        }

        for (int bit = 0; bit < ButtonsCount; bit++)
        {
            var button = (ChiakiControllerButton)(1u << bit);
            bool was = previous.Buttons.HasFlag(button);
            bool is_ = now.Buttons.HasFlag(button);

            if (was != is_)
                events.Add(HistoryEvent.Press(button, is_ ? Pressed : (byte)0));
        }

        // The triggers carry their LEVEL, and are compared by it rather than by a bit.
        if (previous.L2 != now.L2)
            events.Add(HistoryEvent.Press(ChiakiControllerButton.L2, now.L2));

        if (previous.R2 != now.R2)
            events.Add(HistoryEvent.Press(ChiakiControllerButton.R2, now.R2));

        return events;
    }

    /// <summary>
    /// Whether the change produced anything, which is the C's history_dirty.
    ///
    /// Kept as its own question because the sender's flush turns on it: a change with no events is
    /// a state to send and not a history packet to format.
    /// </summary>
    public static bool Dirties(PadSnapshot previous, PadSnapshot now) => Record(previous, now).Count > 0;
}
