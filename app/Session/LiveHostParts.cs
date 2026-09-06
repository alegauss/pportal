namespace ChiakiNg.Session;

/// <summary>Where a part of a live run host comes from when the console is real.</summary>
public enum PartSupply
{
    /// <summary>Composed from managed pieces that have shipped, with nothing else needed.</summary>
    Composed,

    /// <summary>Composed, but from something only the C session holds - a key, a spec, an address.</summary>
    FromTheSession,

    /// <summary>Nothing builds one yet, so the composition root cannot supply it.</summary>
    Missing,
}

/// <summary>One constructor parameter of the run host, and what a live session would hand it.</summary>
/// <param name="Parameter">The parameter's name, as the constructor spells it.</param>
/// <param name="Supply">Which of the three kinds of answer it has.</param>
/// <param name="Supplier">What builds it, named so a reader can go and look.</param>
/// <param name="Why">What decides that, because a row with no reason is a table.</param>
public readonly record struct LiveHostPart(
    string Parameter, PartSupply Supply, string Supplier, string Why);

/// <summary>
/// PP765, under PP762: what a run host costs when the console is real.
///
/// PP762 owes a composition root and nobody knew what one cost. The host takes eleven parts, every
/// test builds them from doubles, and the question a live session asks of each - where does this
/// come from - had never been written down. PP696 shipped without anybody asking it, which is how a
/// green commit left a client that could not stream.
///
/// MOSTLY YES, WHICH IS THE MEASUREMENT AND NOT THE HOPE. The takion takes a tag and draws the
/// rest; the peer is the address discovery already found; the congestion control, the feedback
/// sender, the events and the message sink compose from things that shipped. StreamOutbound is a
/// real IVideoReceiverOutbound and the receiver takes a handler, so a picture has somewhere to go.
///
/// AND ONE PART IS NOT WHAT IT LOOKS LIKE. The host takes the BIG as a factory and every test
/// passes it a heartbeat - so the call that STARTS a stream has never been built by anything this
/// port runs. BigMessage.Encode can build one; what it wants is the launch spec and the session's
/// crypt, which is the first place a composition root reaches into the C instead of composing
/// managed pieces.
///
/// ASSERTED AGAINST THE CONSTRUCTOR, so this cannot quietly describe an older signature: a
/// parameter added, renamed or reordered is a row somebody has to answer for.
/// </summary>
public static class LiveHostParts
{
    /// <summary>The type whose constructor this is about.</summary>
    public const string HostTypeName = "ChiakiNg.Protocol.ManagedStreamRunHost";

    /// <summary>
    /// The eleven, in the constructor's own order.
    ///
    /// In order deliberately: the list reads as the signature, so a reader comparing the two is
    /// comparing two sequences rather than two sets, and a reordering is visible.
    /// </summary>
    public static IReadOnlyList<LiveHostPart> All { get; } =
    [
        new(
            "takion", PartSupply.Composed, "new ManagedTakion(tag)",
            "It takes a tag and draws its own handshake material; the tag is per session and random."),
        new(
            "peer", PartSupply.FromTheSession, "the address discovery answered with",
            "The console's endpoint, which the connect info already carries and nothing managed finds."),
        new(
            "congestion", PartSupply.Composed, "new ManagedCongestionControl(new ManagedPacketStats(), sink, lossMax)",
            "PP749 shipped the report and its sink; the loss maximum rides on the connect info."),
        new(
            "feedback", PartSupply.Composed, "new ManagedFeedbackSender(sink)",
            "PP723's sender over a sink that puts its two packets on the takion."),
        new(
            "events", PartSupply.Composed, "new ManagedSessionEvents()",
            "PP747 gave it an implementation; what listens is the session's own event callback."),
        new(
            "messages", PartSupply.Composed, "new TakionMessageSink(takion)",
            "PP748 put the run's built messages onto a real wire through this."),
        new(
            "stages", PartSupply.Composed, "the baseline's statistics sink",
            "PP712 settled that the four stage timings belong to the run and the fifth to the session."),
        new(
            "big", PartSupply.FromTheSession, "BigMessage.Encode, over the launch spec and the crypt",
            "THE ONE THAT IS NOT WHAT IT LOOKS LIKE: every test passes a heartbeat here, so the call "
                + "that starts a stream has never been built by anything this port runs."),
        new(
            "video", PartSupply.Composed, "new ManagedVideoReceiver(handler, new StreamOutbound(messages))",
            "PP684 gave the outbound seam its first real implementation, and the handler is where a decoder goes."),
        new(
            "audio", PartSupply.Composed, "new ManagedAudioReceiverPair(frames, frames)",
            "PP740's pair, whose frames PP751's decoder takes."),
        new(
            "haptics", PartSupply.Composed, "the same pair, armed for haptics",
            "One type serves both arms; which it is is the arm it was created for."),
    ];

    /// <summary>The parts a live session must reach into the C for.</summary>
    public static IReadOnlyList<LiveHostPart> FromTheSession { get; } =
        [.. All.Where(one => one.Supply == PartSupply.FromTheSession)];

    /// <summary>And the ones nothing builds yet, which is what would block a composition root.</summary>
    public static IReadOnlyList<LiveHostPart> Missing { get; } =
        [.. All.Where(one => one.Supply == PartSupply.Missing)];
}
