namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: the reorder queue in managed code, which is what turns a UDP arrival order back into a
/// stream.
///
/// This is the module a rewrite would most confidently write from scratch, because it looks like a
/// ring buffer with a window. What it is is a ring buffer indexed by RFC 1982 arithmetic, with a
/// drop policy that fires on four separate occasions - a packet older than the window, a duplicate,
/// an overflow at the low end and an overflow at the high end - and which of the four a push takes
/// IS the behaviour. So this is a transcription of reorderqueue.c rather than an implementation of
/// its description, and <see cref="NativeReorderQueue"/> is kept alongside as the oracle.
///
/// Four things are transcribed and would not have been chosen:
///
///   `ge` and `le` are BUILT from the RFC comparisons - `a == b || gt(a, b)` - so they inherit
///   PP149's antipode: at exactly half the sequence space apart, `ge` is false even though `lt` is
///   false too. The push has a branch that exists only for that case, and the C comment says so;
///
///   an overflow drops from the END by default. The other strategy walks the window's start
///   forward, callback by callback, until there is room - and only then, if the queue emptied,
///   rebases to the new packet;
///
///   `Dispose` fires the drop callback for everything still queued, because
///   chiaki_reorder_queue_fini does. A managed queue that simply went out of scope would lose those
///   notifications, and the port's video path counts them;
///
///   and <see cref="Drop"/> does not remove anything. See its own note - that is libchiaki's
///   behaviour and it is reproduced, not fixed.
/// </summary>
public sealed class ReorderQueue : IDisposable
{
    private readonly List<ReorderDrop> drops = [];
    private readonly bool[] set;
    private readonly long[] payloads;
    private readonly int sizeExp;

    private ulong begin;
    private ulong count;
    private bool disposed;

    /// <param name="sizeExp">The window is 2^sizeExp elements.</param>
    /// <param name="seqNumStart">The sequence number of the first element expected.</param>
    public ReorderQueue(int sizeExp, ushort seqNumStart)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeExp);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeExp, 16);

        this.sizeExp = sizeExp;
        set = new bool[1 << sizeExp];
        payloads = new long[1 << sizeExp];
        begin = seqNumStart;
        count = 0;
        DropStrategy = ReorderDropStrategy.End;
    }

    /// <summary>Everything the queue has dropped, oldest first.</summary>
    public IReadOnlyList<ReorderDrop> Drops => drops;

    /// <summary>The window's capacity, 2^sizeExp.</summary>
    public int Size => 1 << sizeExp;

    /// <summary>How many slots the window currently spans, set or not.</summary>
    public ulong Count => count;

    /// <summary>Which end an overflow drops from. END by default, as the C init sets it.</summary>
    public ReorderDropStrategy DropStrategy { get; set; }

    private int Mask => (1 << sizeExp) - 1;

    private int Index(ulong seqNum) => (int)(seqNum & (ulong)Mask);

    /// <summary>The 16-bit add the queue is initialised with: wraps at the counter's width.</summary>
    private static ulong Add(ulong a, ulong b) => (ushort)((ushort)a + (ushort)b);

    private static bool Gt(ulong a, ulong b) => SeqNum.Gt((ushort)a, (ushort)b);

    private static bool Lt(ulong a, ulong b) => SeqNum.Lt((ushort)a, (ushort)b);

    /// <summary>
    /// `a == b || gt(a, b)`, which is NOT `!lt(a, b)`.
    ///
    /// PP149's antipode reaches the queue through exactly this macro: at half the sequence space
    /// apart both comparisons are false, so `ge` is false and so is `lt`, and the push below needs a
    /// branch for the case that is neither.
    /// </summary>
    private static bool Ge(ulong a, ulong b) => a == b || Gt(a, b);

    /// <summary>A packet arriving. Which of the four drop occasions it takes is the behaviour.</summary>
    public void Push(ulong seqNum, long payload)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        ulong end = Add(begin, count);

        // Inside the window: fill the slot, unless it is already filled - a duplicate.
        if (Ge(seqNum, begin) && Lt(seqNum, end))
        {
            int at = Index(seqNum);
            if (set[at])
            {
                Drop(seqNum, payload);
                return;
            }

            payloads[at] = payload;
            set[at] = true;
            return;
        }

        // Older than the window.
        if (Lt(seqNum, begin))
        {
            Drop(seqNum, payload);
            return;
        }

        if (!Ge(seqNum, end))
        {
            // The antipode. Sequence comparisons are undefined at half the serial-number space, so
            // this is neither inside the window, older than it, nor after it. The C rebases only
            // when the queue is empty AND the caller opted into dropping from the begin; otherwise
            // it drops rather than aborting.
            if (count == 0 && DropStrategy == ReorderDropStrategy.Begin)
            {
                begin = seqNum;
                end = seqNum;
            }
            else
            {
                Drop(seqNum, payload);
                return;
            }
        }

        ulong freeElems = (ulong)Size - count;
        ulong totalEnd = Add(end, freeElems);
        ulong newEnd = Add(seqNum, 1);

        if (Lt(totalEnd, newEnd))
        {
            if (DropStrategy == ReorderDropStrategy.End)
            {
                Drop(seqNum, payload);
                return;
            }

            // Drop from the start until there is room or the queue empties. Every set slot passed
            // over is its own callback, in window order.
            while (count > 0 && Lt(totalEnd, newEnd))
            {
                int at = Index(begin);
                if (set[at])
                    Drop(begin, payloads[at]);

                // NOT cleared. The C leaves `set` alone here and the slot is only reset when the
                // window later grows back over it, in the loop below. Clearing it looks like
                // tidying up and is a behaviour change: with a small window, two sequence numbers
                // 2^sizeExp apart share a slot, so whether a stale `set` survives decides whether
                // the next arrival at that index reads as a duplicate.
                begin = Add(begin, 1);
                count--;
                freeElems = (ulong)Size - count;
                totalEnd = Add(end, freeElems);
            }

            // Emptied rather than made room: start again at the new packet.
            if (count == 0)
                begin = seqNum;
        }

        // Grow the window to cover the new packet, clearing each slot on the way.
        end = Add(begin, count);
        while (Lt(end, newEnd))
        {
            count++;
            set[Index(end)] = false;
            end = Add(begin, count);
        }

        payloads[Index(seqNum)] = payload;
        set[Index(seqNum)] = true;
    }

    /// <summary>
    /// The next element in order, or null when the window's head has not arrived.
    ///
    /// Null for an empty queue AND for a queue whose first slot is a gap, which are different
    /// states answering the same way: the caller cannot pull past a missing packet, which is what
    /// makes this a reorder queue rather than a buffer.
    /// </summary>
    public (ulong SeqNum, long Payload)? Pull()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (count == 0)
            return null;

        int at = Index(begin);
        if (!set[at])
            return null;

        // The slot's `set` stays TRUE. chiaki_reorder_queue_pull does not clear it either - the
        // window moves past it instead, and the grow loop in Push is what resets the slot if the
        // window ever comes back round to it.
        var result = (begin, payloads[at]);
        begin = Add(begin, 1);
        count--;
        return result;
    }

    /// <summary>
    /// The element at an OFFSET from the window's start - not at a sequence number, which is the
    /// mistake the parameter name in libchiaki shouts about.
    /// </summary>
    public (ulong SeqNum, long Payload)? Peek(ulong index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (index >= count)
            return null;

        ulong seqNum = Add(begin, index);
        int at = Index(seqNum);
        return set[at] ? (seqNum, payloads[at]) : null;
    }

    /// <summary>
    /// Reports one element as dropped, by offset - and does NOT remove it.
    ///
    /// chiaki_reorder_queue_drop fires the callback and then, for the last element only, runs
    /// `while(!entry->set)` to shrink the count. But nothing ever clears `entry->set`: the entry it
    /// tests is the one it just reported, whose `set` is true by the guard three lines above. So the
    /// loop cannot execute, the count never shrinks, and the element is still there to be pulled.
    ///
    /// Reproduced, not fixed. The port is a translation and the video path is written against this
    /// behaviour; a queue that really removed the element here would differ from the client every
    /// caller was tested with. The condition is spelled out below so the dead loop is visible
    /// rather than tidied away.
    /// </summary>
    public void Drop(ulong index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (index >= count)
            return;

        ulong seqNum = Add(begin, index);
        int at = Index(seqNum);
        if (!set[at])
            return;

        Drop(seqNum, payloads[at]);

        if (index == count - 1)
        {
            // `set[at]` is true - the guard above returned otherwise, and nothing has cleared it -
            // so this never runs. It is libchiaki's loop, kept where libchiaki has it.
            while (!set[at])
            {
                count--;
                if (count == 0)
                    break;

                seqNum = Add(begin, count - 1);
                at = Index(seqNum);
            }
        }
    }

    /// <summary>
    /// Reports everything still queued as dropped, which is what chiaki_reorder_queue_fini does.
    ///
    /// Not a formality. A managed queue that simply went out of scope would lose those callbacks,
    /// and they are how the video path learns a frame will never complete.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        for (ulong i = 0; i < count; i++)
        {
            ulong seqNum = Add(begin, i);
            int at = Index(seqNum);
            if (set[at])
                Drop(seqNum, payloads[at]);
        }

        disposed = true;
    }

    private void Drop(ulong seqNum, long payload) => drops.Add(new ReorderDrop(seqNum, payload));
}
