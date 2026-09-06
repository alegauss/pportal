using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP756: the pad's state, going somewhere that is not the struct PP696 deletes.
///
/// PadFeed is the port's only live caller of chiaki_session_set_controller_state, and that function
/// hands what it takes to stream_connection.feedback_sender. So the one path a person actually plays
/// through was wired into the half of session.c that is about to stop existing, with no seam to
/// point elsewhere. These hold the seam and the second thing that satisfies it.
///
/// THE HOLD IS THE PART A PORT DROPS. The C stores the state on the session unconditionally and
/// forwards it only when feedback_sender_active, and streamconnection.c replays the stored one when
/// the sender comes up. A sink that merely forwarded would pass every test about a running stream
/// and lose every push made before one started.
/// </summary>
public class ManagedControllerStateSinkTests
{
    private sealed class Silent : IFeedbackSink
    {
        public void SendState(ushort seqNum, FeedbackMotion state)
        {
        }

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
        {
        }
    }

    /// <summary>A state that is not idle in any of the three groups the snapshot reads.</summary>
    private static ChiakiControllerState Moved()
    {
        var state = new ChiakiControllerState();

        state.Buttons = ChiakiControllerButton.Cross;
        state.Triggers = (0x40, 0x80);
        state.Sticks = (1000, -2000, 3000, -4000);
        state.SetMotion(
            gyroX: 0.25f, gyroY: -0.5f, gyroZ: 0.75f,
            accelX: -1.5f, accelY: 0.5f, accelZ: 2.5f,
            orientX: 0.1f, orientY: 0.2f, orientZ: 0.3f, orientW: 0.9f);

        return state;
    }

    /// <summary>
    /// A SNAPSHOT OFF A LIVE STATE CARRIES ALL THREE GROUPS, which is what the new reader is for.
    ///
    /// PadSnapshot.From has existed since the recorder and reads buttons, triggers and touches. The
    /// motion half had no reader at all, so a snapshot built before this carried ten zeroes and four
    /// zero sticks - motion control and both thumbsticks switched off, on a path whose only symptom
    /// is a game that does not respond to either.
    /// </summary>
    [Fact]
    public void ASnapshotOffALiveStateCarriesMotionAndSticksToo()
    {
        using ChiakiControllerState state = Moved();

        FeedbackSnapshot snapshot = FeedbackSnapshot.From(state);

        Assert.Equal(ChiakiControllerButton.Cross, snapshot.Pad.Buttons);
        Assert.Equal(0x40, snapshot.Pad.L2);
        Assert.Equal(0x80, snapshot.Pad.R2);

        Assert.Equal(1000, snapshot.Motion.LeftX);
        Assert.Equal(-2000, snapshot.Motion.LeftY);
        Assert.Equal(3000, snapshot.Motion.RightX);
        Assert.Equal(-4000, snapshot.Motion.RightY);

        Assert.Equal(0.25f, snapshot.Motion.GyroX);
        Assert.Equal(-0.5f, snapshot.Motion.GyroY);
        Assert.Equal(0.75f, snapshot.Motion.GyroZ);
        Assert.Equal(-1.5f, snapshot.Motion.AccelX);
        Assert.Equal(0.5f, snapshot.Motion.AccelY);
        Assert.Equal(2.5f, snapshot.Motion.AccelZ);

        Assert.Equal(0.9f, snapshot.Motion.OrientW);

        // And it is not the resting snapshot, which a reader returning zeroes would have produced.
        Assert.NotEqual(FeedbackSnapshot.Idle, snapshot);
    }

    /// <summary>
    /// A PUSH WITH NOTHING ARMED IS KEPT AND IS A SUCCESS, which is the C's own branch.
    ///
    /// chiaki_session_set_controller_state assigns session-&gt;controller_state before it tests
    /// feedback_sender_active, and returns CHIAKI_ERR_SUCCESS either way. A sink that refused, or
    /// that dropped the state, would make a pad pressed during the connect screen invisible.
    /// </summary>
    [Fact]
    public void APushBeforeAnySenderIsHeldRatherThanDropped()
    {
        var sink = new ManagedControllerStateSink();
        using ChiakiControllerState state = Moved();

        Assert.False(sink.Active);
        Assert.Equal(FeedbackSnapshot.Idle, sink.Held);

        Assert.Equal(ChiakiError.Success, sink.SetControllerState(state));

        Assert.False(sink.Active);
        Assert.Equal(FeedbackSnapshot.From(state), sink.Held);
    }

    /// <summary>
    /// ARMING REPLAYS WHAT IS HELD, which is streamconnection.c's line after the sender's init.
    ///
    /// Asserted through the sender's own answer rather than through a field: pushing the same
    /// snapshot at it a second time returns false, its early return for a state that did not move.
    /// A sink that armed without replaying would leave the sender on idle, and that same push would
    /// answer true.
    /// </summary>
    [Fact]
    public void ArmingHandsTheHeldStateToTheSenderThatJustStarted()
    {
        var sink = new ManagedControllerStateSink();
        using var sender = new ManagedFeedbackSender(new Silent());
        using ChiakiControllerState state = Moved();

        sink.SetControllerState(state);
        sink.Arm(sender);

        Assert.True(sink.Active);
        Assert.False(sender.SetControllerState(sink.Held));
    }

    /// <summary>And with one armed, a push reaches it rather than only being held.</summary>
    [Fact]
    public void APushWithASenderArmedReachesIt()
    {
        var sink = new ManagedControllerStateSink();
        using var sender = new ManagedFeedbackSender(new Silent());
        using ChiakiControllerState state = Moved();

        sink.Arm(sender);
        Assert.Equal(ChiakiError.Success, sink.SetControllerState(state));

        Assert.False(sender.SetControllerState(FeedbackSnapshot.From(state)));
    }

    /// <summary>
    /// Disarming keeps the state, because the pad has not stopped reporting.
    ///
    /// The C has no disarm - feedback_sender_active goes false with the stream connection, and
    /// session-&gt;controller_state outlives it, which is the whole reason the replay on the next
    /// start has something to replay.
    /// </summary>
    [Fact]
    public void DisarmingLeavesTheHeldStateWhereItIs()
    {
        var sink = new ManagedControllerStateSink();
        using var sender = new ManagedFeedbackSender(new Silent());
        using ChiakiControllerState state = Moved();

        sink.Arm(sender);
        sink.SetControllerState(state);
        sink.Disarm();

        Assert.False(sink.Active);
        Assert.Equal(FeedbackSnapshot.From(state), sink.Held);
    }

    /// <summary>
    /// And the C session satisfies the same seam, which is what PadFeed still runs through today.
    ///
    /// Asserted by assignment and not by a call: reaching chiaki_session_set_controller_state needs
    /// a session, and what this is about is that PadFeed no longer names one.
    /// </summary>
    [Fact]
    public void TheCSessionIsTheOtherThingThatSatisfiesTheSeam()
    {
        Assert.True(typeof(IControllerStateSink).IsAssignableFrom(typeof(ChiakiSession)));
        Assert.True(typeof(IControllerStateSink).IsAssignableFrom(typeof(ManagedControllerStateSink)));

        // And the feed takes the seam, not either of them.
        Assert.Equal(
            typeof(IControllerStateSink),
            typeof(PadFeed).GetConstructors().Single().GetParameters().Single().ParameterType);
    }
}
