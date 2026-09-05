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
/// ONE SUBSYSTEM IS OWED. PP712 counted four; PP714, PP719 and PP723 answered three of them, so
/// what is left is a BIG message. It is its own piece of work rather than a stub, and this list
/// falling is what shipping one looks like from here.
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
            "SendBig",
            HostAnswer.Owed,
            null,
            "PP712: nothing assembles one. A BIG carries the session key, the launch spec, the encrypted key and an ECDH public key with its signature."),
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

    /// <summary>The members nothing managed answers for yet, by name.</summary>
    public static IReadOnlyList<string> Owed { get; } =
    [
        .. Members.Where(one => one.How == HostAnswer.Owed)
            .Select(one => one.Member)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The subsystems those members belong to, which is what a plan is made from.
    ///
    /// The count that matters is objects and not members: PP714 took congestion control off this
    /// list and two members with it, because a start and a stop are one thread. PP723 took three at
    /// once for the same reason - a sender's init, its fini and the counter lifted out of it are one
    /// object. PP719 took exactly one, and was the largest of the three: nothing managed raised a
    /// session event at all, so the run's one CONNECTED needed a seam nine events wide.
    /// </summary>
    public static IReadOnlyList<string> OwedSubsystems { get; } = ["a BIG message"];

    /// <summary>The interface's file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);
}
