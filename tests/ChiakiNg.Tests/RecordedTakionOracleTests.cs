using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP785: the four managed modules against what takion's C answered, on EVERY build.
///
/// PP780 measured what PP27's end state costs and the shim was the largest consumer by a long way -
/// eighteen of the twenty-four symbols the deletion leaves undefined, twice what lib's own four
/// files hold between them. Those wrappers are not consumers in the ordinary sense: the reorder
/// queue, the send buffer, the MAC gate and both AV parsers exist there so a managed replacement can
/// be held against the C it replaces. So deleting takion.c deletes the second opinion along with the
/// thing it is an opinion about.
///
/// PP785's first half made every one of those differentials ASK before it calls, which stops the
/// deletion turning them red. It does not stop them going quiet: an early return in xUnit is a pass,
/// and a build with no oracle would report the same green with nothing compared.
///
/// SO THE ANSWERS ARE RECORDED, which is PP33's answer one library over. tests/oracles/takion.json
/// holds what the C said for the cases in <see cref="TakionOracleCases"/>, taken by
/// --record-takion-oracle while the wrappers were here, and this file compares the managed side
/// against it with no guard at all. What declines on the day of the deletion is the LIVE second
/// opinion - one test at the bottom of this file, which is what keeps the recording from becoming a
/// table somebody typed.
/// </summary>
public class RecordedTakionOracleTests
{
    /// <summary>The recording, or a failure that says how to take one.</summary>
    private static TakionOracleRecording Recording()
    {
        TakionOracleRecording? recording = TakionOracleRecording.Read();

        Assert.True(
            recording is not null,
            $"{TakionOracleRecording.RelativePath} is missing or unreadable, so takion's answers are "
                + "gone and nothing is comparing this port against them. Build a shim with "
                + "CHIAKI_SHIM_HAVE_TAKION and run --record-takion-oracle.");

        return recording;
    }

    /// <summary>The queue scripts, by name, so a failure names a shape rather than an index.</summary>
    public static TheoryData<string> QueueScripts()
    {
        var data = new TheoryData<string>();

        foreach (QueueScript script in TakionOracleCases.Queues)
            data.Add(script.Name);

        return data;
    }

    /// <summary>The send buffer's, likewise.</summary>
    public static TheoryData<string> BufferScripts()
    {
        var data = new TheoryData<string>();

        foreach (BufferScript script in TakionOracleCases.SendBuffers)
            data.Add(script.Name);

        return data;
    }

    /// <summary>The four lead bytes both AV parsers are asked about.</summary>
    public static TheoryData<byte> AvLeads()
    {
        var data = new TheoryData<byte>();

        foreach (byte lead in TakionOracleCases.AvLeads)
            data.Add(lead);

        return data;
    }

    /// <summary>
    /// PP785: the recording holds an answer for every case, and the answers are not all refusals.
    ///
    /// PP271's rule as it applies to a recorded oracle: a file of nulls is one every comparison below
    /// would agree with, and a comparison that agrees with anything asserts nothing. The recorder
    /// refuses to write one, and this is the same claim made about the file that is in the tree.
    /// </summary>
    [Fact]
    public void TheRecordingAnswersEveryCaseAndNotOnlyWithRefusals()
    {
        TakionOracleRecording recording = Recording();

        Assert.Equal(TakionOracleCases.Queues.Count, recording.Queues.Count);
        Assert.Equal(TakionOracleCases.SendBuffers.Count, recording.SendBuffers.Count);
        Assert.Equal(TakionOracleCases.Mac.Count, recording.Mac.Count);
        Assert.Equal(TakionOracleCases.Av.Count, recording.V9.Count);
        Assert.Equal(TakionOracleCases.Av.Count, recording.V7.Count);

        Assert.Contains(recording.Queues, queue => queue.Drops.Count > 0);
        Assert.Contains(recording.Queues, queue => queue.Pulls.Any(pull => pull is not null));
        Assert.Contains(recording.SendBuffers, buffer => buffer.Errors.Contains(ChiakiError.Overflow));
        Assert.Contains(recording.Mac, row => row.Error == ChiakiError.Success);
        Assert.Contains(recording.Mac, row => row.Error != ChiakiError.Success);
        Assert.Contains(recording.V9, row => row.Packet is not null);
        Assert.Contains(recording.V7, row => row.Header is not null);
    }

    /// <summary>
    /// PP785: the managed reorder queue does what reorderqueue.c did, step for step.
    ///
    /// The count after EVERY operation, every pull's answer, the whole drop log with payloads, and
    /// the final window offset by offset - including one past the end, which is the offset both have
    /// to refuse the same way. Four observables because a queue that dropped the right COUNT of the
    /// wrong packets would pass any one of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(QueueScripts))]
    public void TheManagedQueueRepeatsWhatTheCDid(string name)
    {
        QueueScript script = TakionOracleCases.Queues.Single(one => one.Name == name);

        QueueAnswer? recorded = Recording().Queue(name);
        Assert.True(recorded is not null, $"no recorded answer for the queue script \"{name}\"");

        QueueAnswer was = recorded!;

        ReorderQueue queue = script.Width == ReorderWidth.Sixteen
            ? new ReorderQueue(script.SizeExp, (ushort)script.Start)
            : ReorderQueue.Wide(script.SizeExp, script.Start);

        try
        {
            queue.DropStrategy = script.Strategy;

            Assert.Equal(was.Size, queue.Size);

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

            Assert.Equal(was.Counts, counts);
            Assert.Equal(was.Pulls, pulls);

            // Snapshotted before anything disposes: the managed queue reports everything still
            // queued at teardown and the native one is silent there on purpose, which is the one
            // divergence PP23 states and the reason the log is read while both are open.
            Assert.Equal(
                was.Drops,
                queue.Drops.Select(drop => new QueueSlot(drop.SeqNum, drop.Payload)).ToList());

            var window = new List<QueueSlot?>();
            for (ulong offset = 0; offset <= queue.Count; offset++)
                window.Add(Slot(queue.Peek(offset)));

            Assert.Equal(was.Window, window);
        }
        finally
        {
            queue.Dispose();
        }
    }

    /// <summary>
    /// PP785: the managed send buffer holds what takionsendbuffer.c held, after every step.
    ///
    /// The count and the push's error, which is PP125's finding about what is observable at all:
    /// ChiakiTakionSendBufferPacket is incomplete in the public header, so which packets remain
    /// cannot be asked - and every property worth asserting turns out to be expressible in the
    /// count, the wrap included.
    ///
    /// An ack's error is compared to Success rather than to the model. The C returns one and the
    /// model returns what it released, so deriving a model error from the C's would be an assertion
    /// that only ever restates its own input.
    /// </summary>
    [Theory]
    [MemberData(nameof(BufferScripts))]
    public void TheManagedSendBufferRepeatsWhatTheCDid(string name)
    {
        BufferScript script = TakionOracleCases.SendBuffers.Single(one => one.Name == name);

        SendBufferAnswer? recorded = Recording().SendBuffer(name);
        Assert.True(recorded is not null, $"no recorded answer for the buffer script \"{name}\"");

        SendBufferAnswer was = recorded!;
        var managed = new TakionSendBuffer(script.Capacity);

        for (int i = 0; i < script.Steps.Count; i++)
        {
            BufferStep step = script.Steps[i];

            if (step.IsPush)
            {
                Assert.Equal(
                    was.Errors[i],
                    managed.Push(step.SeqNum, TakionOracleCases.SendBufferPacketSize));
            }
            else
            {
                Assert.Equal(ChiakiError.Success, was.Errors[i]);
                managed.Ack(step.SeqNum);
            }

            Assert.True(
                was.Counts[i] == managed.Count,
                $"step {i} ({(step.IsPush ? "push" : "ack")} {step.SeqNum:x8}): "
                    + $"the C held {was.Counts[i]} and the model holds {managed.Count}");
        }
    }

    /// <summary>
    /// PP785: the managed MAC gate leaves the packet chiaki_takion_packet_mac left.
    ///
    /// The verdict, the four bytes copied out, and the WHOLE packet afterwards. The last one is the
    /// reason the recording holds the packet rather than the changed span: "wrote the field" and
    /// "wrote nothing else" are one claim, and a gate that zeroed the right four bytes and one more
    /// would pass a comparison of the field alone.
    /// </summary>
    [Fact]
    public void TheManagedMacGateWritesWhatTheCWrote()
    {
        TakionOracleRecording recording = Recording();
        int compared = 0;

        foreach ((int baseType, int size) in TakionOracleCases.Mac)
        {
            MacAnswer? recorded = recording.MacRow(baseType, size);
            Assert.True(recorded is not null, $"no recorded answer for type {baseType} at {size} bytes");

            MacAnswer was = recorded.Value;
            byte[] packet = TakionOracleCases.MacPacket(baseType, size);

            TakionPacketMac.MacResult managed =
                TakionPacketMac.Apply(packet, gmac: null, wantMacBefore: true);

            Assert.Equal(was.Error, managed.Error);
            Assert.Equal(was.MacBefore, managed.MacBefore is null ? null : Hex(managed.MacBefore));
            Assert.Equal(was.After, Hex(packet));

            compared++;
        }

        // PP271: a sweep that compared nothing would agree with anything.
        Assert.Equal(TakionOracleCases.Mac.Count, compared);
    }

    /// <summary>
    /// PP785: the managed v9 parse answers what takion.c's v9 arm answered, field for field.
    ///
    /// A managed key position ledger rather than the C's, which is what makes this run on a build
    /// with no wrappers at all - PP677 transcribed chiaki_key_state and asserts it against the C
    /// separately, so the ledger is not what this test is about.
    /// </summary>
    [Theory]
    [MemberData(nameof(AvLeads))]
    public void TheManagedV9ParseAnswersWhatTheCAnswered(byte lead)
    {
        TakionOracleRecording recording = Recording();
        int agreed = 0, refusedBoth = 0;

        foreach (int size in TakionOracleCases.AvSizes)
        {
            AvV9Answer? recorded = recording.V9Row(lead, size);
            Assert.True(recorded is not null, $"no recorded v9 answer for 0x{lead:x2} at {size} bytes");

            AvV9Answer was = recorded.Value;

            AvPacket? managed = AvPacketParse.Parse(
                false, new ManagedKeyState(), TakionOracleCases.AvPacketBytes(lead, size),
                out ChiakiError error);

            Assert.Equal(was.Error, error);

            if (was.Packet is null)
            {
                Assert.Null(managed);
                refusedBoth++;
                continue;
            }

            Assert.NotNull(managed);
            Assert.Equal(was.Packet.Value, managed.Value);
            agreed++;
        }

        // PP271 again: this passed by refusing everything, or by never reaching the bound, unless
        // both outcomes occur.
        Assert.True(agreed > 0, "no length parsed, so the agreement above is about refusals only");
        Assert.True(refusedBoth > 0, "no length was refused, so the bound was never exercised");
    }

    /// <summary>
    /// PP785: and the v7 parse, over the same bytes - which is a different reading of them.
    ///
    /// The same datagrams deliberately. v7 has no audio arm and its bound counts the nalu-info add
    /// on both, so a managed v7 body that had quietly become v9's would agree with the recorded v9
    /// answers and disagree with these.
    /// </summary>
    [Theory]
    [MemberData(nameof(AvLeads))]
    public void TheManagedV7ParseAnswersWhatTheCAnswered(byte lead)
    {
        TakionOracleRecording recording = Recording();
        int agreed = 0, refusedBoth = 0;

        foreach (int size in TakionOracleCases.AvSizes)
        {
            AvV7Answer? recorded = recording.V7Row(lead, size);
            Assert.True(recorded is not null, $"no recorded v7 answer for 0x{lead:x2} at {size} bytes");

            AvV7Answer was = recorded.Value;

            V7AvHeader? managed = AvPacketV7.Parse(
                TakionOracleCases.AvPacketBytes(lead, size), out ChiakiError error);

            Assert.Equal(was.Error, error);

            if (was.Header is null)
            {
                Assert.Null(managed);
                refusedBoth++;
                continue;
            }

            Assert.NotNull(managed);
            Assert.Equal(was.Header.Value, managed.Value);
            agreed++;
        }

        Assert.True(agreed > 0, "no length parsed, so the agreement above is about refusals only");
        Assert.True(refusedBoth > 0, "no length was refused, so the bound was never exercised");
    }

    /// <summary>
    /// PP785: AND THE RECORDING IS STILL WHAT THE WRAPPERS SAY, on a build that can ask.
    ///
    /// This is what keeps the file from becoming a fossil. A recorded second opinion nobody checks is
    /// a table somebody typed, and the whole reason it is worth having is that it came off the C - so
    /// while a build carrying takion.c exists, it is re-derived and compared. It declines where the
    /// wrappers are absent, which is the one guard PP27's deletion leaves standing here and the only
    /// one whose absence costs nothing: what it protects is asserted above on every build.
    /// </summary>
    [Fact]
    public void TheRecordingIsWhatTheWrappersStillSay()
    {
        if (!ShimTakionShape.OracleIsAvailable())
            return;

        TakionOracleRecording? taken = TakionOracleRecorder.Take();
        Assert.NotNull(taken);

        TakionOracleRecording was = Recording();

        // Field by field, because a record's generated equality compares its list members by
        // reference - two identical recordings would differ, and a difference that is always there
        // is a check nobody can act on.
        Assert.Equal(was.Mac, taken.Mac);
        Assert.Equal(was.V9, taken.V9);
        Assert.Equal(was.V7, taken.V7);

        Assert.Equal(was.Queues.Count, taken.Queues.Count);
        for (int i = 0; i < was.Queues.Count; i++)
        {
            Assert.Equal(was.Queues[i].Name, taken.Queues[i].Name);
            Assert.Equal(was.Queues[i].Size, taken.Queues[i].Size);
            Assert.Equal(was.Queues[i].Counts, taken.Queues[i].Counts);
            Assert.Equal(was.Queues[i].Pulls, taken.Queues[i].Pulls);
            Assert.Equal(was.Queues[i].Drops, taken.Queues[i].Drops);
            Assert.Equal(was.Queues[i].Window, taken.Queues[i].Window);
        }

        Assert.Equal(was.SendBuffers.Count, taken.SendBuffers.Count);
        for (int i = 0; i < was.SendBuffers.Count; i++)
        {
            Assert.Equal(was.SendBuffers[i].Name, taken.SendBuffers[i].Name);
            Assert.Equal(was.SendBuffers[i].Errors, taken.SendBuffers[i].Errors);
            Assert.Equal(was.SendBuffers[i].Counts, taken.SendBuffers[i].Counts);
        }
    }

    private static QueueSlot? Slot((ulong SeqNum, long Payload)? answer)
        => answer is { } value ? new QueueSlot(value.SeqNum, value.Payload) : null;

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
