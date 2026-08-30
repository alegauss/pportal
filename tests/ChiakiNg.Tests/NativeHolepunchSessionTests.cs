using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP481: the nine asks, run against the real C.
///
/// This is the assertion the line was waiting for. PP429 wrote the nine down, PP479 gave them an
/// interface, PP480 joined the two - and no implementation could ship, because every one of the
/// nine takes a session handle and a handle came only from PSN credentials, a network and a console
/// answering. A test would have asserted that nine P/Invoke declarations exist, which tests the
/// declarations and not the calls.
///
/// chiaki_holepunch_session_set_recorded is what moved that. The five value-returning asks read
/// fields a recorded exchange carries, so a session from the REAL init - which allocates, sets the
/// defaults and creates the pipes and mutexes, and touches nothing remote - answers them with the
/// C's own code over the C's own struct.
///
/// WHAT IS STILL NOT ASSERTED, and is said here rather than left to be discovered: CreateOffer and
/// PunchHole talk to PSN and to the console. They are wrapped and reachable, and nothing below
/// calls them, because a test that did would be testing the network.
/// </summary>
public class NativeHolepunchSessionTests
{
    private static RecordedHolepunch Recording() => new(
        PsIp: "192.168.1.42",
        ClientLocalIp: "192.168.1.7",
        CtrlPort: 9295,
        Data1: [.. Enumerable.Range(0, 16).Select(i => (byte)(i + 1))],
        Data2: [.. Enumerable.Range(0, 16).Select(i => (byte)(0x40 + i))],
        CustomData1: [.. Enumerable.Range(0, 16).Select(i => (byte)(0x80 + i))]);

    /// <summary>
    /// The three value getters answer with what the recording supplied, through the C.
    ///
    /// Distinct values per field on purpose: three sixteen-byte blocks of the same bytes would pass
    /// on a wrapper that copied one of them three times, which is exactly the mistake a hand-written
    /// marshalling layer makes.
    /// </summary>
    [Fact]
    public void TheRecordedValuesComeBackThroughTheC()
    {
        RecordedHolepunch recorded = Recording();
        using var session = NativeHolepunchSession.FromRecording(recorded);

        Assert.Equal(recorded.PsIp, session.GetSelectedAddress());
        Assert.Equal(recorded.CtrlPort, session.GetCtrlPort());

        var info = Assert.IsType<HolepunchRegistInfo>(session.GetRegistInfo());
        Assert.Equal(recorded.Data1, info.Data1);
        Assert.Equal(recorded.Data2, info.Data2);
        Assert.Equal(recorded.CustomData1, info.CustomData1);
        Assert.Equal(recorded.ClientLocalIp, info.LocalIp);
    }

    /// <summary>
    /// Both socket arms answer, they answer differently, and neither answers null.
    ///
    /// PP461 says the getter hands back the address of a field and can never be null; PP429 says the
    /// two calls are told apart only by the port argument. Both are checked here, and the second is
    /// what a wrapper ignoring its argument would fail: it would return the same pointer twice.
    /// </summary>
    [Fact]
    public void TheTwoSocketArmsAreToldApartByThePortType()
    {
        using var session = NativeHolepunchSession.FromRecording(Recording());

        var ctrl = Assert.IsType<IntPtr>(session.GetSocket(HolepunchPortType.Ctrl));
        var data = Assert.IsType<IntPtr>(session.GetSocket(HolepunchPortType.Data));

        Assert.NotEqual(IntPtr.Zero, ctrl);
        Assert.NotEqual(IntPtr.Zero, data);
        Assert.NotEqual(ctrl, data);
    }

    /// <summary>
    /// Fini runs the C's teardown, counts, and leaves nothing to release twice.
    ///
    /// The count is what the flow needs: the session is released from two paths and PP479's outcome
    /// reports how many times it happened. The second call being a no-op is what keeps a Dispose
    /// after a flow's own Fini from being a double free.
    /// </summary>
    [Fact]
    public void FiniReleasesOnceAndIsSafeTwice()
    {
        var session = NativeHolepunchSession.FromRecording(Recording());
        Assert.True(session.IsOpen);

        session.Fini();
        Assert.Equal(1, session.FinisCalled);
        Assert.False(session.IsOpen);

        session.Dispose();
        Assert.Equal(1, session.FinisCalled);
    }

    /// <summary>A released session is asked nothing further, and says so rather than crashing.</summary>
    [Fact]
    public void AskingAReleasedSessionThrowsRatherThanReachingFreedMemory()
    {
        var session = NativeHolepunchSession.FromRecording(Recording());
        session.Fini();

        Assert.Throws<ObjectDisposedException>(() => session.GetCtrlPort());
        Assert.Throws<ObjectDisposedException>(() => session.GetSelectedAddress());
        Assert.Throws<ObjectDisposedException>(() => session.GetSocket(HolepunchPortType.Ctrl));
    }

    /// <summary>
    /// The one step a recording provably cannot answer, held open in code rather than in prose.
    ///
    /// Everything the flow asks before it goes to the real C. This stops at CreateOffer with a
    /// canned refusal, and that is deliberate: run for real, CreateOffer RETURNED SUCCESS on a
    /// recorded session - it builds the offer from state the session already has - and the flow
    /// went on to PunchHole, which is where the console is genuinely needed. Both took three
    /// seconds of network between them.
    ///
    /// So pinning "it fails at CreateOffer" would have been wrong, and pinning "it fails at
    /// PunchHole" would put unbounded network I/O in a suite that bounds its runs for exactly that
    /// reason. What is asserted is the seam: the asks before the boundary answered by the C, one
    /// refused here, one release. Which of the two a live machine stops at is a question for
    /// hardware.
    ///
    /// TWO ASKS REACH THE C HERE, not five, and that is PP460's order rather than a shortfall: only
    /// the ctrl socket and the registration info come before the offer. The address, the port and
    /// the data socket are all AFTER the punch, so the flow cannot reach them without hardware -
    /// which is why the tests above drive them directly instead of through it.
    /// </summary>
    [Fact]
    public void TheFlowDrivesTheRealSessionUpToTheStepThatNeedsHardware()
    {
        using var real = NativeHolepunchSession.FromRecording(Recording());
        var session = new StopsBeforeTheNetwork(real);
        var connect = new HolepunchConnect(session, ctrlSock => ctrlSock);

        HolepunchConnectOutcome outcome = connect.Run();

        Assert.Equal(HolepunchStep.CreateOffer, outcome.FailedAt);
        Assert.Equal(ChiakiError.Unknown, outcome.Error);
        Assert.Equal(1, outcome.FinisCalled);
        Assert.Equal(1, real.FinisCalled);

        // And everything before that step was the C answering, not this wrapper: the ctrl socket
        // and the registration info, which is all PP460's order puts ahead of the offer.
        Assert.Equal(2, session.AnsweredByTheC);
    }

    /// <summary>
    /// The real session for everything a recording can answer, and a refusal for the step that
    /// would go to PSN. Written out rather than mocked so the boundary is one readable class.
    /// </summary>
    private sealed class StopsBeforeTheNetwork(NativeHolepunchSession inner) : IHolepunchSession
    {
        /// <summary>How many asks reached the C. The two network methods never do.</summary>
        public int AnsweredByTheC { get; private set; }

        public object GetSocket(HolepunchPortType type)
        {
            AnsweredByTheC++;
            return inner.GetSocket(type);
        }

        public object GetRegistInfo()
        {
            AnsweredByTheC++;
            return inner.GetRegistInfo();
        }

        public string GetSelectedAddress()
        {
            AnsweredByTheC++;
            return inner.GetSelectedAddress();
        }

        public ushort GetCtrlPort()
        {
            AnsweredByTheC++;
            return inner.GetCtrlPort();
        }

        public ChiakiError CreateOffer() => ChiakiError.Unknown;

        public ChiakiError PunchHole(HolepunchPortType type) => ChiakiError.Unknown;

        public void Fini() => inner.Fini();
    }

    /// <summary>
    /// The seam is joined to the interface it was written for, so the nine sites reach these nine
    /// wrappers rather than a second implementation nobody joined.
    /// </summary>
    [Fact]
    public void TheNativeSessionIsTheInterfacePP480Joined()
    {
        Assert.True(typeof(IHolepunchSession).IsAssignableFrom(typeof(NativeHolepunchSession)));
        Assert.Equal(7, HolepunchSeamJoin.MethodCount);
        Assert.Equal(9, HolepunchSeamJoin.Joins.Count);
    }
}
