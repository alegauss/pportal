using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One decision the C's recorder makes, and where the port makes the same one.</summary>
/// <param name="InTheC">The text of the decision, as feedbacksender.c writes it.</param>
/// <param name="Answers">What in <see cref="FeedbackHistoryRecorder"/> stands for it.</param>
/// <param name="Why">What would be wrong without it.</param>
public readonly record struct RecorderDecision(string InTheC, string Answers, string Why);

/// <summary>
/// PP717: feedback_sender_record_history read out of its own source.
///
/// The function is static, so nothing can call it and no differential can be run against it. What
/// is left is the text - which is the same answer PP669 reached for the frame path and PP712 for
/// the run's host: name each decision, name what answers it, and assert both directions so a
/// decision that leaves the C fails here rather than surviving as managed code nobody re-reads.
///
/// THE ORDER IS PART OF IT. The events go into a ring that formats NEWEST FIRST, so the sequence
/// the diff produces them in is the reverse of the sequence the console reads them in - which means
/// the walk being touches, then buttons low bit upward, then L2, then R2 is a wire fact and not a
/// style. <see cref="Walk"/> is asserted in the C's own text by position.
/// </summary>
public static class FeedbackRecorderSource
{
    /// <summary>Where the recorder is, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\feedbacksender.c";

    /// <summary>The function this reads. Static, which is why it is read rather than called.</summary>
    public const string Function = "feedback_sender_record_history";

    /// <summary>
    /// The walk, in order, as fragments that occur in the function's body.
    ///
    /// Each is asserted to come before the next. A port that emitted buttons before touches would
    /// put them in the opposite order in the formatted packet, because the ring reverses.
    /// </summary>
    public static IReadOnlyList<string> Walk { get; } =
    [
        "i<CHIAKI_CONTROLLER_TOUCHES_MAX",
        "i<CHIAKI_CONTROLLER_BUTTONS_COUNT",
        "CHIAKI_CONTROLLER_ANALOG_BUTTON_L2",
        "CHIAKI_CONTROLLER_ANALOG_BUTTON_R2",
    ];

    /// <summary>Every decision the diff makes, and the managed code that makes it too.</summary>
    public static IReadOnlyList<RecorderDecision> Decisions { get; } =
    [
        new(
            "state_prev->touches[i].id != state_now->touches[i].id && state_prev->touches[i].id >= 0",
            "the first branch of the slot loop",
            "A slot whose finger changed reports the OLD one leaving, at the position it was last seen at."),
        new(
            "chiaki_feedback_history_event_set_touchpad(&event, false, (uint8_t)state_prev->touches[i].id",
            "HistoryEvent.Touch(false, was.Id, was.X, was.Y)",
            "The release carries the PREVIOUS id and the PREVIOUS position, not the new finger's."),
        new(
            "else if(state_now->touches[i].id >= 0",
            "the else on the slot loop",
            "An ELSE, so a slot whose id changed emits one event and not two - the new finger waits."),
        new(
            "state_prev->touches[i].x != state_now->touches[i].x",
            "was.X != is_.X in the second branch",
            "A finger that only moved is an event, which is how a drag reaches the console at all."),
        new(
            "chiaki_feedback_history_event_set_touchpad(&event, true, (uint8_t)state_now->touches[i].id",
            "HistoryEvent.Touch(true, is_.Id, is_.X, is_.Y)",
            "Down, with the current id and position."),
        new(
            "uint64_t button_id = 1 << i",
            "(ChiakiControllerButton)(1u << bit)",
            "The bit itself is the button id, so the sixteen go out lowest first."),
        new(
            "now ? 0xff : 0",
            "is_ ? Pressed : 0",
            "A digital button is all-ones or nothing, with nothing in between."),
        new(
            "state_prev->l2_state != state_now->l2_state",
            "previous.L2 != now.L2",
            "The trigger is compared by LEVEL, so a change from 30 to 200 is an event."),
        new(
            "CHIAKI_CONTROLLER_ANALOG_BUTTON_L2, state_now->l2_state",
            "HistoryEvent.Press(ChiakiControllerButton.L2, now.L2)",
            "And it carries that level rather than 0xff, which is what makes it analog."),
        new(
            "CHIAKI_CONTROLLER_ANALOG_BUTTON_R2, state_now->r2_state",
            "HistoryEvent.Press(ChiakiControllerButton.R2, now.R2)",
            "The same for the right trigger, last in the walk."),
        new(
            "feedback_sender->history_dirty = true",
            "FeedbackHistoryRecorder.Dirties",
            "Any event at all dirties the history, which is what makes the sender format a packet."),
    ];

    /// <summary>The recorder's body, or null outside a checkout.</summary>
    public static string? Body()
    {
        string? path = SanitizerSource.LocateRelative(RelativePath);
        return path is null ? null : CFunction.BodyIn(path, Function);
    }
}
