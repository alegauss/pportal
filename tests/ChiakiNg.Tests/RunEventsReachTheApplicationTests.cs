using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP799: the run's CONNECTED reaching the handler the application actually installed.
///
/// PP719 built the seam whose Send drops an event where nothing listens and keeps the count. PP747
/// built the sink those events land in. Nothing joined them, and the count WAS the record: a live
/// run took sixteen thousand datagrams and decoded 2675 frames while StreamRun waited out its
/// forty-five seconds and reported that the console had not connected.
///
/// WHICH IS THE WORST SHAPE A DEFECT CAN HAVE - nothing red, a console streaming, and the two
/// readings a session offers disagreeing. So these hold the join itself rather than the pieces,
/// which had assertions of their own on both sides of a gap nobody crossed.
/// </summary>
public class RunEventsReachTheApplicationTests(ITestOutputHelper output)
{
    private static ChiakiSession? Session()
    {
        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = "127.0.0.1", Ps5 = true };
        info.SetRegistKey(new byte[16]);
        info.SetMorning(new byte[16]);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);

        return ChiakiSession.TryCreate(info, null, out _);
    }

    /// <summary>
    /// THE RUN'S CONNECTED ARRIVES AT THE SESSION'S OWN HANDLER, which is the whole of the defect.
    ///
    /// Asserted through the seam and not by calling the forward directly: what was broken was that
    /// <see cref="ManagedSessionEvents.Send"/> had no sink, so a test that invoked the hook would
    /// pass on the tree this task found.
    /// </summary>
    [Fact]
    public void WhatTheRunRaisesReachesTheHandlerTheApplicationInstalled()
    {
        using ChiakiSession? session = Session();
        if (session is null)
            return;

        var seen = new List<ChiakiEventType>();
        session.SetEventHandler(one => seen.Add(one.Type));

        var events = new ManagedSessionEvents();

        // Before the join: raised, counted, and gone - which is what a run reported for twenty
        // seconds while the window in front of it showed a spinner.
        Assert.False(events.SendConnected());
        Assert.Equal(1, events.Unheard);
        Assert.Empty(seen);

        SessionEventRouter router = ManagedStreamPhase.ListenerFor(session, events);

        Assert.True(events.SendConnected());
        Assert.Equal([ChiakiEventType.Connected], seen);
        Assert.Equal(1, router.Connected);

        output.WriteLine($"sent {events.Sent}, unheard {events.Unheard}, seen {seen.Count}");
    }

    /// <summary>
    /// AND THE OTHER EIGHT STOP AT THE ROUTER, which is PP747's decision rather than an omission.
    ///
    /// They carry a value the console has just set and the session's handler cannot carry one: the
    /// shim's dispatch hands every non-quit event over as a bare type. So forwarding them would
    /// deliver the word "Rumble" and lose the two motor bytes that are the entire content.
    /// </summary>
    [Fact]
    public void TheEightThatCarryAValueAreHeldRatherThanForwarded()
    {
        using ChiakiSession? session = Session();
        if (session is null)
            return;

        var seen = new List<ChiakiEventType>();
        session.SetEventHandler(one => seen.Add(one.Type));

        var events = new ManagedSessionEvents();
        SessionEventRouter router = ManagedStreamPhase.ListenerFor(session, events);

        Assert.True(events.Send(new SessionEvent(
            ChiakiEventType.Rumble, Rumble: new RumbleState(1, 0x20, 0x30))));

        // Held here, where a pad driver reads it - and not announced as a type with no bytes.
        Assert.Equal(new RumbleState(1, 0x20, 0x30), router.Rumble);
        Assert.Empty(seen);
    }

    /// <summary>
    /// A QUIT IS REFUSED AT THIS DOOR, because it is the one arm that carries something.
    ///
    /// chiaki_shim_session_dispatch decodes the quit arm and flattens every other event to its type,
    /// which is why forwarding the rest loses nothing. A quit raised here would reach a disconnect
    /// screen as reason None with no sentence, and the session thread is the only thing that knows
    /// the real ending.
    /// </summary>
    [Fact]
    public void AQuitCannotBeRaisedThroughTheDoorTheRunUses()
    {
        using ChiakiSession? session = Session();
        if (session is null)
            return;

        session.SetEventHandler(_ => { });

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Raise(ChiakiEventType.Quit));
    }

    /// <summary>
    /// AND A SESSION WITH NO HANDLER DROPS IT, which is chiaki_session_send_event's own behaviour.
    ///
    /// The C returns where no callback is registered rather than failing, so every raiser can be
    /// unconditional. Answering false rather than throwing keeps that property on this side, and
    /// keeps "nobody was listening" a different fact from "nothing happened".
    /// </summary>
    [Fact]
    public void ASessionWithNoHandlerAnswersFalseRatherThanFailing()
    {
        using ChiakiSession? session = Session();
        if (session is null)
            return;

        Assert.False(session.Raise(ChiakiEventType.Connected));
    }

    /// <summary>
    /// AND THE COMPOSITION ROOT IS WHAT CALLS IT, which is the half a unit test cannot reach.
    ///
    /// The root's own composition runs only once a start arrives, so on a machine with no console
    /// the object never composes and nothing can observe the wiring. That is exactly the
    /// gap PP762 fell into - every piece present, the call absent, and a census that counts types
    /// unable to see it - so the call is read out of the file, as code and not as text.
    /// </summary>
    [Fact]
    public void TheRootAttachesTheListenerRatherThanOnlyOfferingOne()
    {
        if (SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs") is not { } path)
            return;

        string code = DeadAssertions.CodeOnly(File.ReadAllText(path));

        Assert.Contains($"{nameof(ManagedStreamPhase.ListenerFor)}(session, events)", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP783 IS WHAT THIS UNBLOCKS, and the reason it is a dependency rather than a neighbour.
    ///
    /// The flip hands the stream phase to the port, and CHIAKI_EVENT_CONNECTED is raised inside
    /// streamconnection.c - so the moment the C stops running the stream, the only session event
    /// StreamRun waits on stops existing. Its wait is not a poll it can outlive: WaitAny answers
    /// non-zero on a timeout and the run returns having drawn nothing.
    /// </summary>
    [Fact]
    public void TheEventTheFlipWouldOtherwiseRemoveIsTheOneForwardedHere()
    {
        Assert.Contains(ChiakiEventType.Connected, ManagedSessionEvents.RaisedByTheFramePath);
        Assert.Contains(ChiakiEventType.Connected, SessionEventRouter.Routed);

        if (StreamPhaseDriver.LocateSession() is not { } session)
            return;

        // The C still raises it today, from the file the flip takes out of the run.
        Assert.True(StreamPhaseDriver.TheCRunsIt(File.ReadAllText(session)));
    }

    /// <summary>The phase still composes with the listener attached, and takes no new argument for it.</summary>
    [Fact]
    public void TheListenerCostsTheConstructorNothing()
    {
        using ChiakiSession? session = Session();
        if (session is null)
            return;

        using var baseline = new SessionBaseline();
        using var phase = new ManagedStreamPhase(session, IPAddress.Loopback, (_, _, _) => true, baseline);

        // Null until a start builds a host, exactly as Arrivals is: a phase nobody handed over to
        // has composed nothing, and a router standing there would say a run had begun.
        Assert.Null(phase.Events);
        Assert.Null(phase.Arrivals);
    }
}
