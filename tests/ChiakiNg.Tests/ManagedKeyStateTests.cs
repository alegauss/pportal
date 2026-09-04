using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP677: the managed key state against the shim's, step by step.
///
/// The oracle is the wrapper PP23 built and PP111 fed the C suite's cases through, and the corpus is
/// PP519's: four thousand real heads whose positions include the twenty-six repeats that make the
/// expansion worth having. Both are run here, committed and peeked, and compared at EVERY step -
/// not at the end, because a state that diverged and came back would agree about its last answer.
/// </summary>
public class ManagedKeyStateTests(ITestOutputHelper output)
{
    private static IReadOnlyList<CapturedDatagram>? Corpus() => DatagramCorpus.Read();

    /// <summary>
    /// Sequences chosen for the two wraps, the repeat, and the guard on the decrement.
    ///
    /// Written as lows because that is what the wire carries; what each demonstrates is in the
    /// comment beside it.
    /// </summary>
    public static TheoryData<string, uint[]> Sequences()
    {
        var data = new TheoryData<string, uint[]>();

        data.Add("from zero, ordinary advances", [0, 0, 0, 0x10, 0x20, 0x30, 0x1000]);

        // The repeat, which neither comparison is true of.
        data.Add("consecutive repeats", [0x1000, 0x1000, 0x1000, 0x1010, 0x1010]);

        // Up over the wrap: the RFC comparison says newer, the plain one says smaller.
        data.Add("wrapping up past 2^32", [0xfffffff0, 0xfffffff8, 0x00000004, 0x00000010]);

        // Down again, which the C also handles - a reordered packet from before the wrap.
        data.Add("wrapping up then back down", [0xfffffff0, 0x00000004, 0xfffffff8, 0x00000010]);

        // The DECREMENT'S GUARD: a low that looks like it wrapped down while high is still zero.
        // Without the guard the high half becomes 0xffffffff and the position is astronomical.
        data.Add("apparent wrap down while high is zero", [0x00000004, 0xfffffff0, 0x00000008]);

        // Half the space apart in both directions, where the RFC comparison is at its boundary.
        data.Add("half the space apart", [0, 0x7fffffff, 0x80000000, 0x80000001, 0xffffffff, 0]);

        // A long climb through several wraps.
        data.Add(
            "several wraps",
            [0x80000000, 0xffffff00, 0x00000100, 0x80000100, 0xffffff00, 0x00000100]);

        return data;
    }

    /// <summary>THE DIFFERENTIAL, committing every step.</summary>
    [Theory]
    [MemberData(nameof(Sequences))]
    public void CommittingEveryStepAgreesWithTheC(string name, uint[] lows)
    {
        output.WriteLine(name);

        using var native = new KeyState();
        var managed = new ManagedKeyState();

        foreach (uint low in lows)
        {
            ulong theirs = native.RequestPos(low, commit: true);
            ulong ours = managed.RequestPos(low, commit: true);

            Assert.Equal(theirs, ours);
        }
    }

    /// <summary>
    /// AND PEEKING, which must leave both states exactly where they were.
    ///
    /// Interleaved rather than run apart: a peek between two commits is what a real parse does, and
    /// a peek that committed would only show up as the NEXT answer being wrong.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sequences))]
    public void PeekingBetweenCommitsAgreesAndAdvancesNothing(string name, uint[] lows)
    {
        output.WriteLine(name);

        using var native = new KeyState();
        var managed = new ManagedKeyState();

        foreach (uint low in lows)
        {
            // Peeked twice with a different value between, so a state that committed the peek
            // would answer the second one differently.
            Assert.Equal(native.RequestPos(low, commit: false), managed.RequestPos(low, commit: false));
            Assert.Equal(native.RequestPos(~low, commit: false), managed.RequestPos(~low, commit: false));
            Assert.Equal(native.RequestPos(low, commit: false), managed.RequestPos(low, commit: false));

            Assert.Equal(native.RequestPos(low, commit: true), managed.RequestPos(low, commit: true));
        }
    }

    /// <summary>
    /// THE CORPUS: every position a real console sent, through both, in arrival order.
    ///
    /// PP519's capture. One ledger and not three - the field sits at a different offset for control
    /// than for AV, and what it holds is a single counter the console advances for everything it
    /// sends, so arrival order is the only order in which the positions are monotonic.
    /// </summary>
    [Fact]
    public void TheCorpusAgreesAtEveryStep()
    {
        if (Corpus() is not { } datagrams)
            return;

        var lows = new List<uint>();
        foreach (CapturedDatagram datagram in datagrams)
        {
            if (TakionPacketMac.ReadKeyPosition(datagram.Head, out uint low) == ChiakiNg.Native.ChiakiError.Success)
                lows.Add(low);
        }

        // PP271: a corpus that yielded nothing would agree with any implementation at all.
        Assert.True(lows.Count > 1000, $"only {lows.Count} position(s) read from the corpus");

        using var native = new KeyState();
        var managed = new ManagedKeyState();

        var repeats = 0;
        for (var i = 0; i < lows.Count; i++)
        {
            if (i > 0 && lows[i] == lows[i - 1])
                repeats++;

            Assert.Equal(native.RequestPos(lows[i], commit: true), managed.RequestPos(lows[i], commit: true));
        }

        output.WriteLine($"{lows.Count} position(s), {repeats} repeat(s), agreed at every step");

        // The repeats are the reason the expansion is run at all, so their absence would mean the
        // corpus stopped exercising the case this is most likely to get wrong.
        Assert.True(repeats > 0, "the corpus carries no repeated position, so the case is untested");
    }

    /// <summary>
    /// The differential can fail, which every comparison above needs to be worth reading.
    ///
    /// An expansion using a plain comparison rather than the RFC one - the mistake this is guarding
    /// against - answers differently on the very first repeat.
    /// </summary>
    [Fact]
    public void ANaiveExpansionDisagreesOnARepeat()
    {
        var managed = new ManagedKeyState();

        managed.RequestPos(0x1000);
        Assert.Equal(0x1000UL, managed.RequestPos(0x1000));

        // What a `>=` would have produced instead, stated so the difference is visible rather than
        // implied: the high half moved, and every byte keyed after it is wrong.
        Assert.NotEqual(0x1_0000_1000UL, managed.RequestPos(0x1000));
    }

    /// <summary>Init is zero, and Commit sets the state outright without expanding.</summary>
    [Fact]
    public void CommitSetsTheStateWithoutExpandingIt()
    {
        var managed = new ManagedKeyState();
        Assert.Equal(0UL, managed.Previous);

        managed.Commit(0x1_2345_6789);
        Assert.Equal(0x1_2345_6789UL, managed.Previous);

        // And the next expansion reads that high half rather than starting again.
        Assert.Equal(0x1_2345_6790UL, managed.RequestPos(0x2345_6790));
    }
}
