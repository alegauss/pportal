using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>PP712: what a member of the run's host has on the managed side.</summary>
public enum HostAnswer
{
    /// <summary>Something does what the call does, and the row names the member that does it.</summary>
    Answered,

    /// <summary>Nothing does, and building it is a piece of work somebody has to take on.</summary>
    Owed,

    /// <summary>
    /// The call has no counterpart because the runtime removes the need for one.
    ///
    /// The three frees and the two lock calls. A managed object is collected and a managed lock is
    /// the language's, so a row naming a type for either would be describing C# rather than
    /// answering for a call - which is exactly the shape PP712 was filed about.
    /// </summary>
    NotNeeded,
}

/// <summary>One member of the run's host, and what stands where it does.</summary>
/// <param name="Member">The member's name, as the interface declares it.</param>
/// <param name="Answer">
/// The counterpart, present only where <paramref name="How"/> is
/// <see cref="HostAnswer.Answered"/> - and then it always names a MEMBER, never a type alone.
/// </param>
/// <param name="Why">What the counterpart stands for, or what the absence costs.</param>
public readonly record struct HostMember(string Member, HostAnswer How, Counterpart? Answer, string Why);

/// <summary>
/// PP707's second criterion: every member of <see cref="IStreamRunHost"/> answered, owed, or needless.
///
/// PP295 wrote the run and PP640 asserted its six orderings, and the host it walks is an interface
/// whose implementations are all in the test project. So the sequence is right and nothing runs it,
/// and PP696 - which stops session.c asking - would leave the application with no stream at all.
///
/// A HOST BUILT WITHOUT THIS CENSUS FINDS THE GAPS ONE COMPILE ERROR AT A TIME, which is the shape
/// PP669 avoided for the frame path: the consumers were mapped by reflection before anything was
/// deleted, and the ones with no counterpart were a decision rather than a surprise.
///
/// PP712: AND A ROW MUST NAME A MEMBER. The first version of this let a counterpart be a type alone,
/// and three rows took the option - SendBig pointed at a builder with no BIG in it, which the check
/// could not see because the TYPE resolved. A counterpart with no member is a claim about a
/// namespace, and PP669's own rule is that a mapping is not a call. So every answered row names the
/// member that does the work, and the ones that cannot say which of the two other things they are.
///
/// NOTHING IS OWED NOW. PP712 counted four subsystems; PP714, PP719, PP723 and PP727 wrote them,
/// and each ship shortened this list in its own commit rather than at the end. What that answers is
/// PP707's SECOND criterion and not its first: every member has a counterpart, and a counterpart is
/// still not a call - nothing in app implements this interface, which is what the first one is about.
///
/// AND OWED IS NOT THE ONLY QUESTION. PP738 added <see cref="SeamOnly"/>, which asks whether the
/// counterpart a row names is reached rather than whether one exists, and found two rows answering
/// with an interface only test doubles implement. PP740 wrote that implementation, so both axes are
/// empty now - and the census says both things rather than reporting the first and being silent
/// about the second, which is what let those two rows read as answered for four commits.
/// </summary>
public static class StreamRunHostConsumers
{
    /// <summary>Where the interface is.</summary>
    public const string RelativePath = @"app\Protocol\ManagedStreamRun.cs";

    /// <summary>Every member, in the order the interface declares them.</summary>
    public static IReadOnlyList<HostMember> Members { get; } =
    [
        new(
            "CreateAudioReceiver",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(IAudioSink), nameof(IAudioSink.Audio)),
            "PP667's seam, which is where a decrypted audio packet goes."),
        new(
            "CreateHapticsReceiver",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(IAudioSink), nameof(IAudioSink.Haptics)),
            "The same seam's other arm: audioreceiver.c is one file used twice."),
        new(
            "CreateVideoReceiver",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedVideoReceiver), nameof(ManagedVideoReceiver.AvPacket)),
            "PP291's receiver, taking the units PP667's route hands it."),
        new(
            "ConnectTakion",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedTakion), nameof(ManagedTakion.Connect)),
            "PP678's, over a socket it owns."),
        new(
            "StartCongestionControl",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedCongestionControl), nameof(ManagedCongestionControl.Start)),
            "PP714's thread, reporting every 200ms out of the stats the two receivers push."),
        new(
            "BeginState",
            HostAnswer.Answered,
            new(
                CounterpartAssembly.App,
                nameof(StreamConnectionStatesSource),
                nameof(StreamConnectionStatesSource.EveryStateStillClearsBothFlags)),
            "PP774: the rule this port already held the C to, now kept by the run as well. The counterpart is that check, because what this member does is the thing it refuses a state for skipping."),
        new(
            "SendBig",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(BigMessage), nameof(BigMessage.Encode)),
            "PP727's message over PP726's spec. PP712 filed this row because a builder with no BIG in it had been answering for it."),
        new(
            "StartFeedbackSender",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedFeedbackSender), nameof(ManagedFeedbackSender.Start)),
            "PP723's thread, which waits the same 200ms window and sends the same two packets."),
        new(
            "Wait",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamConnectionStates), nameof(StreamConnectionStates.Next)),
            "PP362's state walk, which is what decides where a wait's flags lead."),
        new(
            "HasEarlyStreaminfo",
            HostAnswer.Answered,
            new(
                CounterpartAssembly.App,
                nameof(StreamConnectionStates),
                nameof(StreamConnectionStates.IsBufferedWhenEarly)),
            "The one state a message is buffered in rather than handled."),
        new(
            "ReplayEarlyStreaminfo",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamDispatch), nameof(StreamDispatch.Route)),
            "The same routing the live path uses, over the message that arrived too early."),
        new(
            "Unlock",
            HostAnswer.NotNeeded,
            null,
            "A managed lock is the language's. ThreadPrimitives models whether a chiaki mutex can FAIL, which is a different question."),
        new(
            "Lock",
            HostAnswer.NotNeeded,
            null,
            "The same."),
        new(
            "SendConnected",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedSessionEvents), nameof(ManagedSessionEvents.SendConnected)),
            "PP719's seam, which drops an event where nothing is listening rather than failing, as the C's send does."),
        new(
            "WaitIdle",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamIdleLoop), nameof(StreamIdleLoop.Next)),
            "PP363's loop, whose timeout is the work and whose anything-else leaves."),
        new(
            "SendHeartbeat",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamMessages), nameof(StreamMessages.Heartbeat)),
            "PP684's, and its failure is logged and ignored on both sides."),
        new(
            "LiftInputToWire",
            HostAnswer.Answered,
            new(
                CounterpartAssembly.App,
                nameof(ManagedFeedbackSender),
                nameof(ManagedFeedbackSender.LiftInputToWire)),
            "PP723's samples, handed to the destination PP712 found already existing: SessionBaseline.PushInputToWire."),
        new(
            "FiniFeedbackSender",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedFeedbackSender), nameof(ManagedFeedbackSender.Stop)),
            "The same thread's flag, signal and join, in the C's order."),
        new(
            "SendDisconnect",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamMessages), nameof(StreamMessages.Disconnect)),
            "PP684's, sent from the label every failure passes through."),
        new(
            "StopCongestionControl",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedCongestionControl), nameof(ManagedCongestionControl.Stop)),
            "The same thread's signal and join, in that order."),
        new(
            "CloseTakion",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(ManagedTakion), nameof(ManagedTakion.Dispose)),
            "PP678's teardown, in the order PP703 gave it a video queue to release."),
        new(
            "LiftStages",
            HostAnswer.Answered,
            new(CounterpartAssembly.AppSession, nameof(SessionBaseline), nameof(SessionBaseline.PushStage)),
            "Where the four stage timings go. PP680's arm already pushes two of the four."),
        new(
            "FreeVideoReceiver",
            HostAnswer.NotNeeded,
            null,
            "A managed receiver is collected. Naming a type for a free would be describing C#."),
        new(
            "FreeHapticsReceiver",
            HostAnswer.NotNeeded,
            null,
            "The same."),
        new(
            "FreeAudioReceiver",
            HostAnswer.NotNeeded,
            null,
            "The same."),
        new(
            "ShouldStop",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamIdleLoop), nameof(StreamIdleLoop.Outcome)),
            "One of the two flags the idle loop's outcome is decided by."),
        new(
            "RemoteDisconnected",
            HostAnswer.Answered,
            new(CounterpartAssembly.App, nameof(StreamIdleLoop), nameof(StreamIdleLoop.Outcome)),
            "The other."),
    ];

    /// <summary>
    /// PP738: the members answered by a seam nothing in app implements.
    ///
    /// A SECOND AXIS, AND NOT THE ONE ABOVE. <see cref="Owed"/> asks whether a counterpart exists
    /// at all, and it is empty. This asks whether the one named is REACHED, which is PP734's
    /// question one census over - and on it two rows answer with a shape.
    ///
    /// Both were IAudioSink. ManagedAvArm and StreamAvDispatch take one as a parameter, so the
    /// interface was consumed in app while the only things implementing it were doubles in the test
    /// project. PP669's rule is that a mapping is not a call, and an interface member with no
    /// implementation outside the tests is a mapping one step further out.
    ///
    /// PP740 EMPTIED IT, which is what this list said would close it - "a row leaving is the commit
    /// that gave the audio path an implementation". <see cref="ManagedAudioReceiverPair"/> is that
    /// implementation: audioreceiver.c's eight-slot jitter buffer, twice, because the C holds two
    /// receivers and this interface is one object with two methods.
    ///
    /// Kept rather than deleted with its last entry, for the reason Owed is: what would report a
    /// counterpart going back to being a shape nothing fills is this list, not its absence.
    /// </summary>
    public static IReadOnlyList<string> SeamOnly { get; } = [];

    /// <summary>The members nothing managed answers for yet, by name.</summary>
    public static IReadOnlyList<string> Owed { get; } =
    [
        .. Members.Where(one => one.How == HostAnswer.Owed)
            .Select(one => one.Member)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The subsystems those members belong to, which is what a plan is made from - and it is empty.
    ///
    /// The count that mattered was objects and not members: PP714 took congestion control and two
    /// members with it, because a start and a stop are one thread; PP723 took three at once, a
    /// sender's init, its fini and the counter lifted out of it being one object; PP719 took exactly
    /// one and was the largest of the four, since nothing managed raised a session event at all.
    /// PP727 took the last, which needed PP726's launch spec under it.
    ///
    /// Kept rather than deleted with its last entry, because the check that reads it is what would
    /// report a member arriving with nothing to answer for it.
    /// </summary>
    public static IReadOnlyList<string> OwedSubsystems { get; } = [];

    /// <summary>The interface's file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);
}
