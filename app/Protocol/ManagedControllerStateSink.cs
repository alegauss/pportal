using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP756: chiaki_session_set_controller_state, over the port's own feedback sender.
///
/// The C's body is four lines and only one of them forwards. It takes feedback_sender_mutex, stores
/// the state on the SESSION, and hands it to the sender ONLY IF feedback_sender_active - then
/// streamconnection.c replays that stored state at the sender the moment it starts. A port that
/// only forwarded would drop every push made before the stream came up, and the console would open
/// on whatever the pad was doing after that rather than on what it is doing now.
///
/// SO THE HOLD IS THE BEHAVIOUR AND THE FORWARD IS THE OPTIONAL HALF, which is the shape this has:
/// <see cref="Held"/> always moves, <see cref="Arm"/> is the replay, and a push with nothing armed
/// is a success rather than a refusal - the C returns CHIAKI_ERR_SUCCESS in exactly that case.
///
/// THE LOCK IS TAKEN BEFORE THE SENDER'S, as session.c takes it before feedbacksender.c's. The
/// sender takes only its own and calls nothing back, so the two orders never meet.
/// </summary>
public sealed class ManagedControllerStateSink : IControllerStateSink
{
    private readonly object gate = new();

    private ManagedFeedbackSender? sender;
    private FeedbackSnapshot held = FeedbackSnapshot.Idle;

    /// <summary>The last state taken, which is session-&gt;controller_state.</summary>
    public FeedbackSnapshot Held
    {
        get { lock (gate) return held; }
    }

    /// <summary>Whether a sender is armed, which is feedback_sender_active.</summary>
    public bool Active
    {
        get { lock (gate) return sender is not null; }
    }

    /// <summary>
    /// Take the state, and pass it on if there is somebody to pass it to.
    /// </summary>
    /// <returns>
    /// Always success. The C's only failure is the lock's, and a managed monitor has none - so
    /// reporting the sender's own "nothing moved" here would be a code the C never returns.
    /// </returns>
    public ChiakiError SetControllerState(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (gate)
        {
            held = FeedbackSnapshot.From(state);
            sender?.SetControllerState(held);
        }

        return ChiakiError.Success;
    }

    /// <summary>
    /// A sender coming up, handed what is already held - streamconnection.c's line after the init.
    /// </summary>
    public void Arm(ManagedFeedbackSender started)
    {
        ArgumentNullException.ThrowIfNull(started);

        lock (gate)
        {
            sender = started;
            started.SetControllerState(held);
        }
    }

    /// <summary>
    /// And one going away, which leaves the held state where it is: the pad has not stopped
    /// reporting because the stream ended, and the next stream starts from what it last said.
    /// </summary>
    public void Disarm()
    {
        lock (gate)
            sender = null;
    }
}
