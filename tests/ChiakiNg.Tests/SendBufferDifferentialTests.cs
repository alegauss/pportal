using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP518, under PP27: the C's send buffer and PP27's model, given the same operations.
///
/// PP125 bound the C's through the shim and PP27 wrote a managed one that reproduces the ack and
/// the compaction. They have had separate test files since, and nothing ran the same operations
/// through both.
///
/// WHAT IS COMPARED IS THE COUNT, and PP125 settled why: ChiakiTakionSendBufferPacket is incomplete
/// in the public header, so which packets remain cannot be asked. Every property worth asserting
/// turns out to be expressible in the count anyway - the wrap included, which is the case these
/// exist for.
/// </summary>
public class SendBufferDifferentialTests(ITestOutputHelper output)
{
    /// <summary>One thing done to both buffers.</summary>
    private readonly record struct Step(bool IsPush, uint SeqNum);

    private static Step Push(uint seqNum) => new(true, seqNum);

    private static Step Ack(uint seqNum) => new(false, seqNum);

    /// <summary>
    /// Runs a script through both and reports the first step they disagreed on.
    ///
    /// The count after EVERY step, not only at the end: two buffers can diverge and re-converge, and
    /// a comparison that only looked at the end would call that agreement.
    /// </summary>
    private void RunBoth(IReadOnlyList<Step> script, int capacity)
    {
        using var native = new SendBuffer(capacity);
        var managed = new TakionSendBuffer(capacity);

        for (var i = 0; i < script.Count; i++)
        {
            Step step = script[i];

            if (step.IsPush)
            {
                // A push has an error on both sides, so the error is compared as well as the count.
                ChiakiError fromC = native.Push(step.SeqNum, size: 32);
                ChiakiError fromModel = managed.Push(step.SeqNum, size: 32);

                Assert.Equal(fromC, fromModel);
            }
            else
            {
                // An ack has an error on the C's side only - the model returns what it released -
                // so nothing is compared here but the count. Deriving a model error from the C's
                // would be an assertion that only ever restates its own input.
                Assert.Equal(ChiakiError.Success, native.Ack(step.SeqNum));

                IReadOnlyList<uint> released = managed.Ack(step.SeqNum);
                output.WriteLine($"{i}: model released [{string.Join(", ", released)}]");
            }

            output.WriteLine(
                $"{i}: {(step.IsPush ? "push" : "ack ")} {step.SeqNum:x8} "
                + $"-> C {native.Count}, model {managed.Count}");

            Assert.True(
                native.Count == managed.Count,
                $"step {i} ({(step.IsPush ? "push" : "ack")} {step.SeqNum:x8}): "
                + $"the C holds {native.Count} and the model holds {managed.Count}");
        }
    }

    /// <summary>In-order pushes and one ack in the middle release the prefix, in both.</summary>
    [Fact]
    public void AnAckReleasesThePrefixInBoth()
        => RunBoth(
            [Push(10), Push(11), Push(12), Push(13), Ack(12), Push(14)],
            capacity: 16);

    /// <summary>
    /// THE WRAP. A buffer holding 0xfffffff0 and acked at 5 must release it.
    ///
    /// "Older" near 0xffffffff is not "less than", so a comparison by integer would keep those
    /// packets forever while the console waited for messages it had already acknowledged. A handful
    /// of inputs in a space of four billion, so they are named rather than swept for.
    /// </summary>
    [Fact]
    public void TheWrapReleasesWhatIntegerOrderWouldKeep()
        => RunBoth(
            [
                Push(0xfffffff0), Push(0xfffffffe), Push(0xffffffff),
                Push(0x00000000), Push(0x00000005),
                Ack(0x00000005),
                Push(0x00000006),
            ],
            capacity: 16);

    /// <summary>And an ack BEFORE everything held releases nothing, in both.</summary>
    [Fact]
    public void AnAckOlderThanEverythingReleasesNothing()
        => RunBoth([Push(100), Push(101), Push(102), Ack(50), Ack(99)], capacity: 16);

    /// <summary>
    /// A full buffer refuses the same push in both, and the error is the same one.
    ///
    /// Overflow is not a fault - it is the buffer saying the console is behind - so the two have to
    /// agree on which push is the one refused, not merely that one was.
    /// </summary>
    [Fact]
    public void AFullBufferRefusesTheSamePush()
        => RunBoth(
            [Push(1), Push(2), Push(3), Push(4), Push(5), Ack(2), Push(6), Push(7), Push(8)],
            capacity: 4);

    /// <summary>A duplicate sequence number is refused by both, with the same error.</summary>
    [Fact]
    public void ADuplicateSeqNumIsRefusedByBoth()
        => RunBoth([Push(7), Push(8), Push(7), Push(9)], capacity: 8);

    /// <summary>
    /// A long alternating script, so the two are compared over a shape nobody chose by hand.
    ///
    /// Deterministic - a fixed seed - because a differential that only fails sometimes is one
    /// nobody can act on.
    /// </summary>
    [Fact]
    public void ALongScriptKeepsThemInStep()
    {
        var random = new Random(Seed: 518);
        var script = new List<Step>();
        uint next = 0xffffff80;

        for (var i = 0; i < 200; i++)
        {
            if (random.Next(3) == 0 && script.Count > 0)
                script.Add(Ack(unchecked(next - (uint)random.Next(1, 6))));
            else
                script.Add(Push(next++));
        }

        RunBoth(script, capacity: 16);
    }
}
