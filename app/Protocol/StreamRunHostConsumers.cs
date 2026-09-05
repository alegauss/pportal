using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One member of the run's host, and what stands where it does.</summary>
/// <param name="Member">The member's name, as the interface declares it.</param>
/// <param name="Answer">
/// Its counterpart, or null where nothing managed answers for it yet - which is the whole point of
/// the row. An absence here is a decision somebody has to take, not a stub somebody forgot.
/// </param>
/// <param name="Why">What the counterpart stands for, or what the absence costs.</param>
public readonly record struct HostMember(string Member, Counterpart? Answer, string Why);

/// <summary>
/// PP707's second criterion: every member of <see cref="IStreamRunHost"/> answered, or owed.
///
/// PP295 wrote the run and PP640 asserted its six orderings, and the host it walks is an interface
/// whose implementations are all in the test project. So the sequence is right and nothing runs it,
/// and PP696 - which stops session.c asking - would leave the application with no stream at all.
///
/// A HOST BUILT WITHOUT THIS CENSUS FINDS THE GAPS ONE COMPILE ERROR AT A TIME, which is the shape
/// PP669 avoided for the frame path: the consumers were mapped by reflection before anything was
/// deleted, and the two that had no counterpart were a decision rather than a surprise. This is that
/// mapping one interface over.
///
/// TWO ARE OWED AND THEY ARE NOT SMALL. Congestion control is a thread the C starts and stops around
/// the whole run, and the feedback sender is the controller's own path upstream - PP676 ported its
/// serialisers and nothing composes them. Each is its own piece of work and each is named here so
/// building the host is a known cost rather than a discovery.
///
/// A COUNTERPART IS A TYPE THAT RESOLVES AND A MEMBER THAT EXISTS, verified by reflection and never
/// a sentence. The rows that carry none say so with a null, so the census cannot be satisfied by
/// naming something plausible.
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
            new(CounterpartAssembly.App, nameof(IAudioSink)),
            "PP667's seam, which is where a decrypted audio packet goes."),
        new(
            "CreateHapticsReceiver",
            new(CounterpartAssembly.App, nameof(IAudioSink)),
            "The same seam: audioreceiver.c is one file used twice, told apart by which arm calls it."),
        new(
            "CreateVideoReceiver",
            new(CounterpartAssembly.App, nameof(ManagedVideoReceiver)),
            "PP291's receiver, which PP667's route already drives."),
        new(
            "ConnectTakion",
            new(CounterpartAssembly.App, nameof(ManagedTakion), nameof(ManagedTakion.Connect)),
            "PP678's, over a socket it owns."),
        new(
            "StartCongestionControl",
            null,
            "OWED. A thread the C starts around the whole run and nothing managed has one."),
        new(
            "SendBig",
            new(CounterpartAssembly.App, nameof(StreamMessages)),
            "PP684 built the four unsent messages; big is the first thing the run sends."),
        new(
            "StartFeedbackSender",
            null,
            "OWED. PP676 ported feedback.c's serialisers and nothing composes them into a sender."),
        new(
            "Wait",
            new(CounterpartAssembly.App, nameof(StreamConnectionStates)),
            "PP362's state walk, which is what each wait is waiting for."),
        new(
            "HasEarlyStreaminfo",
            new(CounterpartAssembly.App, nameof(StreamConnectionSwitch)),
            "The buffered message the switch decides about, which PP640's fifth ordering is over."),
        new(
            "ReplayEarlyStreaminfo",
            new(CounterpartAssembly.App, nameof(StreamDispatch)),
            "The same handler the live path uses, run over the message that arrived too early."),
        new(
            "Unlock",
            new(CounterpartAssembly.App, nameof(ThreadPrimitives)),
            "PP107's lock model, which is where this port keeps what a chiaki mutex is."),
        new(
            "Lock",
            new(CounterpartAssembly.App, nameof(ThreadPrimitives)),
            "The same."),
        new(
            "SendConnected",
            new(CounterpartAssembly.App, nameof(SessionLifecycle)),
            "The session's event surface, which is where CHIAKI_EVENT_CONNECTED goes."),
        new(
            "WaitIdle",
            new(CounterpartAssembly.App, nameof(StreamIdleLoop)),
            "PP363's loop, whose timeout is the work and whose anything-else leaves."),
        new(
            "SendHeartbeat",
            new(CounterpartAssembly.App, nameof(StreamMessages), nameof(StreamMessages.Heartbeat)),
            "PP684's, and its failure is logged and ignored on both sides."),
        new(
            "LiftInputToWire",
            new(CounterpartAssembly.App, nameof(FeedbackPayload)),
            "What the feedback sender had reached, read out before the fini takes it."),
        new(
            "FiniFeedbackSender",
            null,
            "OWED, with StartFeedbackSender: there is nothing to fini."),
        new(
            "SendDisconnect",
            new(CounterpartAssembly.App, nameof(StreamMessages), nameof(StreamMessages.Disconnect)),
            "PP684's, sent from the label every failure passes through."),
        new(
            "StopCongestionControl",
            null,
            "OWED, with StartCongestionControl."),
        new(
            "CloseTakion",
            new(CounterpartAssembly.App, nameof(ManagedTakion), nameof(ManagedTakion.Dispose)),
            "PP678's teardown, which is what joins the thread that writes the stage counters."),
        new(
            "LiftStages",
            new(CounterpartAssembly.App, nameof(TakionTimingCapture)),
            "The four stage timings, read after the close and before the free."),
        new(
            "FreeVideoReceiver",
            new(CounterpartAssembly.App, nameof(ManagedVideoReceiver)),
            "The receiver's own lifetime, which a managed one ends by going out of scope."),
        new(
            "FreeHapticsReceiver",
            new(CounterpartAssembly.App, nameof(IAudioSink)),
            "The seam's, and the same one twice for the same reason the creates are."),
        new(
            "FreeAudioReceiver",
            new(CounterpartAssembly.App, nameof(IAudioSink)),
            "The same."),
        new(
            "ShouldStop",
            new(CounterpartAssembly.App, nameof(StreamTeardown)),
            "What the disconnect label reads, which decides whether a disconnect is sent at all."),
        new(
            "RemoteDisconnected",
            new(CounterpartAssembly.App, nameof(StreamTeardown)),
            "The other half of the same reading."),
    ];

    /// <summary>The members nothing managed answers for yet, by name.</summary>
    public static IReadOnlyList<string> Owed { get; } =
        [.. Members.Where(one => one.Answer is null).Select(one => one.Member).Order(StringComparer.Ordinal)];

    /// <summary>The interface's file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);
}
