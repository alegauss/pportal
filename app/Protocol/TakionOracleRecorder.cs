using System.Globalization;
using System.Text.Json;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP785: walks <see cref="TakionOracleCases"/> through the real wrappers and writes down every
/// answer.
///
/// Runs only on a build that carries the oracles, which is the point: what it produces is evidence
/// from takion.c, takionsendbuffer.c and reorderqueue.c rather than a table somebody typed. A build
/// without them refuses rather than writing a file of nulls, because a recording that silently
/// recorded nothing is worse than none - the comparisons would then run against it and pass.
///
/// PP33's shape, one oracle over. What differs is only that this one has four kinds of case rather
/// than three, and that a queue's case is a SCRIPT: the observable is what a sequence of pushes and
/// pulls does, so a row is a trace and not an answer.
/// </summary>
public static class TakionOracleRecorder
{
    /// <summary>What a recording attempt did.</summary>
    public enum Outcome
    {
        /// <summary>Written, and the file is the second opinion.</summary>
        Recorded,

        /// <summary>This build has no takion wrappers, so there was nothing to ask.</summary>
        NoOracle,

        /// <summary>The C refused cases the recording exists to hold answers for.</summary>
        OracleDisagreed,
    }

    /// <summary>
    /// Asks the C every case and returns what it said, or null where this build has no oracle.
    /// </summary>
    public static TakionOracleRecording? Take()
    {
        if (!ShimTakionShape.OracleIsAvailable())
            return null;

        return new TakionOracleRecording(
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [.. TakionOracleCases.Queues.Select(AskQueue)],
            [.. TakionOracleCases.SendBuffers.Select(AskSendBuffer)],
            [.. TakionOracleCases.Mac.Select(one => AskMac(one.BaseType, one.Size))],
            [.. TakionOracleCases.Av.Select(one => AskV9(one.Lead, one.Size))],
            [.. TakionOracleCases.Av.Select(one => AskV7(one.Lead, one.Size))]);
    }

    /// <summary>
    /// One script through the C's queue, recording the count after every step.
    ///
    /// The drop log is snapshotted BEFORE the queue is freed. chiaki_reorder_queue_fini reports
    /// everything still queued and the shim unhooks the callback first, so the native queue is silent
    /// at teardown either way - but reading it afterwards would be reading a list whose contents
    /// depend on that, which is a thing to state rather than to rely on.
    /// </summary>
    public static QueueAnswer AskQueue(QueueScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        using NativeReorderQueue queue = script.Width == ReorderWidth.Sixteen
            ? new NativeReorderQueue(script.SizeExp, (ushort)script.Start)
            : NativeReorderQueue.Wide(script.SizeExp, script.Start);

        queue.DropStrategy = script.Strategy;

        var counts = new List<ulong>();
        var pulls = new List<QueueSlot?>();

        foreach (QueueOp op in script.Ops)
        {
            switch (op.Kind)
            {
                case QueueOpKind.Push:
                    queue.Push(op.Value, op.Payload);
                    break;

                case QueueOpKind.Pull:
                    pulls.Add(Slot(queue.Pull()));
                    break;

                default:
                    queue.Drop(op.Value);
                    break;
            }

            counts.Add(queue.Count);
        }

        var window = new List<QueueSlot?>();
        for (ulong offset = 0; offset <= queue.Count; offset++)
            window.Add(Slot(queue.Peek(offset)));

        return new QueueAnswer(
            script.Name,
            queue.Size,
            counts,
            pulls,
            [.. queue.Drops.Select(drop => new QueueSlot(drop.SeqNum, drop.Payload))],
            window);
    }

    /// <summary>One script through the C's send buffer, recording the error and the count each step.</summary>
    public static SendBufferAnswer AskSendBuffer(BufferScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        using var buffer = new SendBuffer(script.Capacity);

        var errors = new List<ChiakiError>();
        var counts = new List<int>();

        foreach (BufferStep step in script.Steps)
        {
            errors.Add(step.IsPush
                ? buffer.Push(step.SeqNum, TakionOracleCases.SendBufferPacketSize)
                : buffer.Ack(step.SeqNum));

            counts.Add(buffer.Count);
        }

        return new SendBufferAnswer(script.Name, errors, counts);
    }

    /// <summary>One packet through the C's MAC gate, with no cipher - the half a unit test can run.</summary>
    public static MacAnswer AskMac(int baseType, int size)
    {
        byte[] packet = TakionOracleCases.MacPacket(baseType, size);
        ChiakiError error = Takion.PacketMacWithoutCrypt(packet, keyPos: 0, out byte[]? before);

        return new MacAnswer(baseType, size, error, before is null ? null : Hex(before), Hex(packet));
    }

    /// <summary>
    /// One datagram through the C's v9 arm.
    ///
    /// A fresh key state each time, because requesting a position commits to a ledger and one parse
    /// would otherwise move the next one's answer.
    /// </summary>
    public static AvV9Answer AskV9(byte lead, int size)
    {
        using var state = new KeyState();

        AvPacket? packet = Takion.ParseV9(
            state, TakionOracleCases.AvPacketBytes(lead, size), out ChiakiError error);

        return new AvV9Answer(lead, size, error, packet);
    }

    /// <summary>
    /// The same datagram through the C's v7 arm, which reads the same bytes differently.
    ///
    /// No key state on either side: the C declares a parameter for one and never reads it, so there
    /// is no ledger to keep in step.
    /// </summary>
    public static AvV7Answer AskV7(byte lead, int size)
    {
        V7AvHeader? header = NativeAvPacketV7.Parse(
            TakionOracleCases.AvPacketBytes(lead, size), out ChiakiError error);

        return new AvV7Answer(lead, size, error, header);
    }

    /// <summary>
    /// Takes a recording and writes it where the comparisons read it.
    /// </summary>
    /// <param name="path">Where to write, or null for <see cref="TakionOracleRecording.RelativePath"/>.</param>
    public static Outcome Write(string? path = null)
    {
        TakionOracleRecording? taken = Take();
        if (taken is null)
            return Outcome.NoOracle;

        // A recording where nothing parsed and nothing was dropped is not a recording of takion, it
        // is a recording of a library that answered nothing - and every comparison would then agree
        // with it. Both halves, because either one alone can be true of a broken oracle.
        bool parsed = taken.V9.Any(row => row.Packet is not null)
            && taken.V7.Any(row => row.Header is not null);

        if (!parsed || !taken.Queues.Any(queue => queue.Drops.Count > 0))
            return Outcome.OracleDisagreed;

        string destination = path ?? Destination();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(taken, TakionOracleRecording.Format));

        return Outcome.Recorded;
    }

    /// <summary>
    /// Where the recording goes: beside the checkout's other oracles, or the working directory.
    ///
    /// The file has to be committed, so the checkout is the only useful destination - but a run from
    /// outside one should produce the file rather than throw, and say where it put it.
    /// </summary>
    public static string Destination()
        => SanitizerSource.RepositoryRoot() is { } root
            ? Path.Combine(root, TakionOracleRecording.RelativePath)
            : Path.GetFullPath(Path.GetFileName(TakionOracleRecording.RelativePath));

    private static QueueSlot? Slot((ulong SeqNum, long Payload)? answer)
        => answer is { } value ? new QueueSlot(value.SeqNum, value.Payload) : null;

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
