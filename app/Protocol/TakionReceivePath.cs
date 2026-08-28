namespace ChiakiNg.Protocol;

/// <summary>
/// What a datagram cost the receive path, as counters rather than as a list.
///
/// A struct with fields, because the whole point of this type is that describing what happened
/// must not allocate. The models under PP487 and PP493 return lists and that is correct for them;
/// this is the path those models describe, and it is charged for what it builds.
/// </summary>
public struct TakionReceiveCounters
{
    /// <summary>Datagrams handed to the path.</summary>
    public int Seen;

    /// <summary>Rejected by the MAC gate, before any branch.</summary>
    public int MacRejected;

    /// <summary>Control packets.</summary>
    public int Control;

    /// <summary>AV packets held back because the cipher was not there yet.</summary>
    public int Postponed;

    /// <summary>Video packets queued for reordering.</summary>
    public int Video;

    /// <summary>Audio packets handed straight on.</summary>
    public int Audio;

    /// <summary>Base types the dispatch does not name.</summary>
    public int UnknownType;

    /// <summary>Bytes copied out because a branch keeps them past the call.</summary>
    public long CopiedBytes;
}

/// <summary>
/// Where a branch that keeps a datagram puts it. Implemented by the queues, mocked by a test.
///
/// An interface, and it is called through one per datagram - which is a virtual call and not an
/// allocation. What WOULD allocate is enumerating one, or capturing one in a lambda, so this hands
/// over spans and returns nothing.
/// </summary>
public interface ITakionSink
{
    /// <summary>Take a copy of a datagram that must outlive the buffer it arrived in.</summary>
    void Keep(TakionDispatchBranch branch, ReadOnlySpan<byte> datagram);

    /// <summary>Borrow a datagram for the length of this call only.</summary>
    void Borrow(TakionDispatchBranch branch, ReadOnlySpan<byte> datagram);
}

/// <summary>
/// PP500, under PP27: the composed receive path - the one piece of this transport that is not a
/// model.
///
/// PP485 through PP499 modelled takion end to end and NONE of them is this. They are harnesses:
/// <see cref="TakionReceiveLoop"/> builds a List of the steps it took, <see cref="TakionDataDrain"/>
/// returns lists of outcomes and deliveries, <see cref="TakionPacketMac"/> copies the MAC out so a
/// test can compare it. Each device exists to make a claim assertable, and each one allocates.
///
/// That is right for a model and wrong for the thing PP44 set a budget for, so this is a separate
/// line rather than a refactor of six. One entry point: a datagram already sitting in PP485's
/// rented buffer, the MAC verdict, the branch, the branch's work - and nothing built to describe
/// what it did.
///
/// WHAT IT MUST NOT DO IS A SHORT LIST, AND EVERY ITEM IS A WAY C# ALLOCATES UNWATCHED. No trace
/// list. No byte array per outcome. No lambda capturing a local, because that closure is an object
/// per call. No params array. No enumerator over an interface. The counters are fields on a struct
/// passed by reference, and the sink takes spans.
///
/// THE ONE COPY THAT IS SUPPOSED TO HAPPEN is the one PP490 named: three of the six branches keep
/// the datagram past the call, and over a reused buffer keeping means copying. That copy is the
/// sink's, charged to <see cref="TakionReceiveCounters.CopiedBytes"/> so a test can say which
/// branches paid it - a path that copied for every branch would still pass a zero-allocation check
/// on this side while doubling the work.
/// </summary>
public static class TakionReceivePath
{
    /// <summary>
    /// Handles one datagram.
    /// </summary>
    /// <param name="datagram">The bytes as they arrived, in the loop's rented buffer.</param>
    /// <param name="sink">Where the branch's work happens.</param>
    /// <param name="counters">Updated in place.</param>
    /// <param name="macOk">Whether the MAC gate passed. False is decided before any branch.</param>
    /// <param name="enableCrypt">The C's `takion->enable_crypt`.</param>
    /// <param name="cryptAvailable">Whether the remote cipher exists.</param>
    public static TakionDispatchBranch Handle(
        ReadOnlySpan<byte> datagram,
        ITakionSink sink,
        ref TakionReceiveCounters counters,
        bool macOk,
        bool enableCrypt,
        bool cryptAvailable)
    {
        ArgumentNullException.ThrowIfNull(sink);

        counters.Seen++;

        // The C asserts buf_size > 0 here; an empty datagram never reaches this in the loop, which
        // PP488 pinned as the thing that ends the thread instead.
        if (datagram.IsEmpty)
        {
            counters.UnknownType++;
            return TakionDispatchBranch.UnknownType;
        }

        TakionDispatchVerdict verdict = TakionDispatch.Decide(
            TakionDispatch.BaseTypeOf(datagram), macOk, enableCrypt, cryptAvailable);

        Count(ref counters, verdict.Branch);

        if (verdict.Lifetime == DatagramLifetime.Copied)
        {
            counters.CopiedBytes += datagram.Length;
            sink.Keep(verdict.Branch, datagram);
        }
        else
        {
            sink.Borrow(verdict.Branch, datagram);
        }

        return verdict.Branch;
    }

    /// <summary>
    /// A switch rather than a dictionary, because a dictionary lookup on an enum boxes it.
    ///
    /// The kind of line that looks like premature care and is the whole subject of this task.
    /// </summary>
    private static void Count(ref TakionReceiveCounters counters, TakionDispatchBranch branch)
    {
        switch (branch)
        {
            case TakionDispatchBranch.MacRejected:
                counters.MacRejected++;
                break;
            case TakionDispatchBranch.Control:
                counters.Control++;
                break;
            case TakionDispatchBranch.Postponed:
                counters.Postponed++;
                break;
            case TakionDispatchBranch.Video:
                counters.Video++;
                break;
            case TakionDispatchBranch.Audio:
                counters.Audio++;
                break;
            default:
                counters.UnknownType++;
                break;
        }
    }
}
