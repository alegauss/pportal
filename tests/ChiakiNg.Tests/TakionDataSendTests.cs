using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP496, under PP27: the two data sends, and the order that decides what each failure costs.
///
/// Every failure here happens after something irreversible, and two of the six outcomes leave a
/// sequence number the console will wait for that nothing can resend. One of those two reports an
/// error. The other reports success.
/// </summary>
public class TakionDataSendTests
{
    /// <summary>A send against a fresh buffer, with the counter starting at <paramref name="from"/>.</summary>
    private static (TakionSendOutcome Outcome, uint Next, TakionSendBuffer Buffer) Run(
        uint from = 100,
        TakionSendVariant variant = TakionSendVariant.First,
        bool keyPositionAvailable = true,
        bool allocationSucceeds = true,
        bool sequenceLockSucceeds = true,
        bool sendSucceeds = true,
        int capacity = 16)
    {
        var buffer = new TakionSendBuffer(capacity);
        uint next = from;

        TakionSendOutcome outcome = TakionDataSend.Send(
            buffer, ref next, payloadSize: 40, variant,
            keyPositionAvailable, allocationSucceeds, sequenceLockSucceeds, sendSucceeds);

        return (outcome, next, buffer);
    }

    /// <summary>A send that works spends both numbers and leaves the packet held for resend.</summary>
    [Fact]
    public void AGoodSendSpendsBothNumbersAndIsHeld()
    {
        (TakionSendOutcome outcome, uint next, TakionSendBuffer buffer) = Run();

        Assert.Equal(TakionSendStage.SentAndHeld, outcome.Stage);
        Assert.Equal(ChiakiError.Success, outcome.Error);
        Assert.True(outcome.KeyPositionSpent);
        Assert.True(outcome.SequenceNumberSpent);
        Assert.Equal(100u, outcome.SeqNum);
        Assert.Equal(101u, next);
        Assert.Equal([100u], buffer.SeqNums);
        Assert.False(TakionDataSend.LeavesAGap(outcome));
    }

    /// <summary>
    /// A refused key position is the only failure that costs nothing, because it is the first thing
    /// tried.
    /// </summary>
    [Fact]
    public void ARefusedKeyPositionSpendsNothing()
    {
        (TakionSendOutcome outcome, uint next, TakionSendBuffer buffer) =
            Run(keyPositionAvailable: false);

        Assert.Equal(TakionSendStage.KeyPositionRefused, outcome.Stage);
        Assert.False(outcome.KeyPositionSpent);
        Assert.False(outcome.SequenceNumberSpent);
        Assert.Equal(100u, next);
        Assert.Empty(buffer.SeqNums);
    }

    /// <summary>
    /// A failed allocation has already moved the key ledger, because the position is taken before
    /// the buffer exists.
    /// </summary>
    [Fact]
    public void AFailedAllocationHasAlreadyMovedTheKeyLedger()
    {
        (TakionSendOutcome outcome, uint next, _) = Run(allocationSucceeds: false);

        Assert.Equal(TakionSendStage.AllocationFailed, outcome.Stage);
        Assert.Equal(ChiakiError.Memory, outcome.Error);
        Assert.True(outcome.KeyPositionSpent);
        Assert.False(outcome.SequenceNumberSpent);
        Assert.Equal(100u, next);
    }

    /// <summary>
    /// THE HOLE: a failed send has spent both numbers, freed the packet, and put nothing in the
    /// send buffer - so the resend loop has nothing to retry.
    /// </summary>
    [Fact]
    public void AFailedSendLeavesAGapNothingCanResend()
    {
        (TakionSendOutcome outcome, uint next, TakionSendBuffer buffer) = Run(sendSucceeds: false);

        Assert.Equal(TakionSendStage.SendFailed, outcome.Stage);
        Assert.Equal(ChiakiError.Network, outcome.Error);
        Assert.True(outcome.KeyPositionSpent);
        Assert.True(outcome.SequenceNumberSpent);
        Assert.Equal(100u, outcome.SeqNum);
        Assert.Equal(101u, next);
        Assert.Empty(buffer.SeqNums);
        Assert.True(TakionDataSend.LeavesAGap(outcome));
    }

    /// <summary>
    /// AND THE QUIET ONE: a full send buffer means the message was sent, was freed, will never be
    /// retried - and the caller is told it succeeded.
    ///
    /// This is the outcome that separates "reported" from "happened". The error is Success.
    /// </summary>
    [Fact]
    public void AFullSendBufferLosesTheMessageAndReportsSuccess()
    {
        var buffer = new TakionSendBuffer(capacity: 2);
        uint next = 50;

        for (var i = 0; i < 2; i++)
            TakionDataSend.Send(buffer, ref next, payloadSize: 8);

        TakionSendOutcome outcome = TakionDataSend.Send(buffer, ref next, payloadSize: 8);

        Assert.Equal(TakionSendStage.SentButNotHeld, outcome.Stage);
        Assert.Equal(ChiakiError.Success, outcome.Error);
        Assert.True(TakionDataSend.LeavesAGap(outcome));

        // Two held, three numbers spent.
        Assert.Equal([50u, 51u], buffer.SeqNums);
        Assert.Equal(53u, next);
    }

    /// <summary>
    /// The unreachable leak, asked for explicitly so the model's note has something behind it.
    ///
    /// A failure to lock the sequence mutex returns without freeing the packet. It cannot happen -
    /// the mutex is an ordinary one - and it is the same shape PP474 and PP491 repaired where it
    /// could.
    /// </summary>
    [Fact]
    public void TheSequenceLockFailureIsTheOneThatLeaks()
    {
        (TakionSendOutcome outcome, uint next, _) = Run(sequenceLockSucceeds: false);

        Assert.Equal(TakionSendStage.SequenceLockFailed, outcome.Stage);
        Assert.True(outcome.PacketLeaked);
        Assert.True(outcome.KeyPositionSpent);
        Assert.False(outcome.SequenceNumberSpent);
        Assert.Equal(100u, next);
    }

    /// <summary>And it is the only outcome that leaks.</summary>
    [Fact]
    public void NoOtherOutcomeLeaks()
    {
        foreach (bool key in new[] { true, false })
        foreach (bool alloc in new[] { true, false })
        foreach (bool send in new[] { true, false })
        {
            (TakionSendOutcome outcome, _, _) =
                Run(keyPositionAvailable: key, allocationSucceeds: alloc, sendSucceeds: send);

            Assert.False(outcome.PacketLeaked);
        }
    }

    /// <summary>
    /// The two variants differ by one byte, and it is the byte the receiving side reads as a type.
    ///
    /// Nine against eight, and a packet one byte longer for the same payload.
    /// </summary>
    [Fact]
    public void TheContinuationOmitsTheTypeByte()
    {
        Assert.Equal(9, TakionDataSend.PayloadOffsetFor(TakionSendVariant.First));
        Assert.Equal(8, TakionDataSend.PayloadOffsetFor(TakionSendVariant.Continuation));

        Assert.True(TakionDataSend.WritesTypeByte(TakionSendVariant.First));
        Assert.False(TakionDataSend.WritesTypeByte(TakionSendVariant.Continuation));

        Assert.Equal(
            TakionDataSend.PacketSize(TakionSendVariant.First, 100) - 1,
            TakionDataSend.PacketSize(TakionSendVariant.Continuation, 100));
    }

    /// <summary>
    /// The byte the continuation omits is the offset PP493's drain reads a data type from.
    ///
    /// Named as a join rather than left as two eights in two files: they are the same eight.
    /// </summary>
    [Fact]
    public void TheOmittedByteIsWhereTheDrainReadsTheType()
        => Assert.Equal(
            TakionDataDrain.DataTypeOffset,
            TakionDataSend.PayloadOffsetFor(TakionSendVariant.Continuation));

    /// <summary>
    /// THE DRIFT CHECK: the C still runs the five steps in the order that makes all of this true,
    /// in both variants.
    ///
    /// If the push ever moved above the send, a failed send would leave something to retry, and
    /// every claim above would be about a bug that no longer exists.
    /// </summary>
    [Theory]
    [InlineData(TakionSendVariant.First)]
    [InlineData(TakionSendVariant.Continuation)]
    public void TheCStillRunsTheStepsInThisOrder(TakionSendVariant variant)
    {
        if (TakionDataSendSource.Locate() is not { } path)
            return;

        string body = Assert.IsType<string>(
            TakionDataSendSource.BodyOf(File.ReadAllText(path), variant));

        Assert.True(TakionDataSendSource.TheStepsAreStillInThisOrder(body));
        Assert.True(TakionDataSendSource.ThePushResultIsDiscarded(body));
        Assert.True(TakionDataSendSource.AFailedSendFreesThePacket(body));
        Assert.True(TakionDataSendSource.TheSequenceLockFailureStillLeaks(body));
    }

    /// <summary>And the C's two variants still differ in exactly the byte this claims.</summary>
    [Fact]
    public void TheCsTwoVariantsStillDifferByTheTypeByte()
    {
        if (TakionDataSendSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string first = Assert.IsType<string>(
            TakionDataSendSource.BodyOf(source, TakionSendVariant.First));
        string cont = Assert.IsType<string>(
            TakionDataSendSource.BodyOf(source, TakionSendVariant.Continuation));

        Assert.True(TakionDataSendSource.TheVariantsDifferByTheTypeByte(first, cont));
    }

    /// <summary>
    /// And the push still frees what it refuses, which is why a full buffer loses the message
    /// rather than holding a stale pointer.
    /// </summary>
    [Fact]
    public void ThePushStillFreesWhatItRefuses()
    {
        if (TakionDataSendSource.LocateSendBuffer() is not { } path)
            return;

        Assert.True(TakionDataSendSource.ThePushFreesWhatItRefuses(File.ReadAllText(path)));
    }
}
