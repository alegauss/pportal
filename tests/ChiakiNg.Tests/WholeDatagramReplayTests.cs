using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP633: the receive loop's budget, held over a datagram that had a payload.
///
/// PP113, PP114 and PP176 measured PP44's budget over the frame processor. This is the stage they
/// sit behind, and it had never been fed anything but eighteen-byte heads - so the copy the keeping
/// branches do was work nothing had ever made it do.
///
/// A reading and not a re-runnable check, for PP608's reason: the capture behind it is a live
/// session's whole traffic, which is not a thing to commit. What a test can hold is that the reading
/// is internally consistent and that the decision it rests on has not moved.
/// </summary>
public class WholeDatagramReplayTests
{
    /// <summary>
    /// PP633: THE NUMBER THE TASK IS FOR. The loop allocated nothing over real payloads.
    ///
    /// Zero rather than small. PP44's budget is not a ceiling on this path - a pause at the wrong
    /// moment is a dropped frame, and the traffic is thousands of small packets a second - so an
    /// allocation per packet would be thousands a second whatever its size.
    /// </summary>
    [Fact]
    public void TheLoopAllocatedNothingOverRealPayloads()
    {
        Assert.Equal(0, WholeDatagramReplay.BytesAllocatedAfterWarmUp);

        // And it really did copy: a reading of zero copied bytes would be the head-only replay
        // wearing this task's name.
        Assert.True(WholeDatagramReplay.BytesCopied > 3_000_000);
    }

    /// <summary>
    /// PP633: the copy is less than the wire total, which is what says a header is read rather than
    /// copied.
    ///
    /// The two numbers being equal would mean the loop copies whole datagrams, and the difference is
    /// exactly the part a head-only capture reports as nothing at all.
    /// </summary>
    [Fact]
    public void TheKeepingBranchesCopyBodiesAndNotHeaders()
    {
        Assert.True(
            WholeDatagramReplay.BytesCopied < WholeDatagramReplay.WireBytesAcrossChannels,
            "the loop copied at least as much as arrived, so it is copying headers too");

        // And more than the video alone, because audio is kept as well.
        Assert.True(WholeDatagramReplay.BytesCopied > WholeDatagramReplay.Video.WireBytes);
    }

    /// <summary>
    /// PP633: the channels add up to the datagrams read, so the reading is of one run.
    ///
    /// Two numbers from separate lines of one report is how a transcription goes wrong, and a total
    /// that did not match would mean a channel had been dropped in the copying.
    /// </summary>
    [Fact]
    public void TheChannelsAccountForEveryDatagram()
    {
        Assert.Equal(WholeDatagramReplay.Datagrams, WholeDatagramReplay.PacketsAcrossChannels);

        // Video is nearly all of it, which is what makes the payload question a video question.
        Assert.True(WholeDatagramReplay.Video.WireBytes
            > WholeDatagramReplay.WireBytesAcrossChannels * 0.9);
    }

    /// <summary>
    /// PP633: and the gate's number did not move, which is the question this run would otherwise
    /// leave open.
    ///
    /// PP497's gate reads to offset eighteen and stops, so it is a head operation whatever the
    /// datagram's length. Asserted as the SHARE of the gap rather than the microseconds, which is
    /// what PP610 settled: a second machine keeps the ratio and not the clock.
    /// </summary>
    [Fact]
    public void TheGateIsAHeadOperationAndItsShareIsUnchanged()
    {
        Assert.True(WholeDatagramReplay.GateManagedMicros > WholeDatagramReplay.GateNativeMicros);

        // Under a twentieth of a percent of the gap, which is PP610's conclusion taken again over
        // traffic that has bodies.
        Assert.True(
            WholeDatagramReplay.GateShareOfGap < 0.0005,
            $"the gate takes {WholeDatagramReplay.GateShareOfGap:P4} of the mean gap");
    }

    /// <summary>
    /// PP635: the loop has no ratio, and the criterion says why rather than staying open on it.
    ///
    /// PP27 asked for "the same comparison for the loop, not just the gate". The gate is comparable
    /// because the C's is a pure function of eighteen bytes. The loop's is not reachable at all -
    /// takion.c's receive handlers are file-local and removing a `static` is the patch a non-goal
    /// refuses - so a wall-clock comparison would time two I/O paths and call the difference the
    /// port's.
    ///
    /// What is asserted is the join: the criterion in the roadmap carries the reason, so a reader
    /// meets it where they decide what finishing means rather than three files away.
    /// </summary>
    [Fact]
    public void TheCriterionSaysWhyTheLoopHasNoRatio()
    {
        if (ChiakiNg.Session.SanitizerSource.LocateRelative(@"docs\ROADMAP.md") is not { } path)
            return;

        // Collapsed, because roadkeep reflows a criterion's reason to the prose width - so the
        // sentence is there and a line break sits inside it. A check that read the file as written
        // would be asserting about the wrapping.
        string roadmap = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(path), @"\s+", " ");

        Assert.Contains("PP635", roadmap, StringComparison.Ordinal);
        Assert.Contains(
            "the gate is comparable and the loop is not", roadmap, StringComparison.Ordinal);

        // And the headroom the answer rests on holds in the reading itself.
        Assert.True(WholeDatagramReplay.GateShareOfGap < WholeDatagramReplay.WorkShareOfGapCeiling);
    }

    /// <summary>
    /// PP633: and the corpus still keeps heads, which is the decision this reading rests on.
    ///
    /// PP608 gives the reason and it has not changed: a head is committable - no account, no
    /// console, no frame - and a whole-datagram capture of a live session is none of those. A corpus
    /// that grew payloads would be this task quietly reversing somebody else's decision.
    ///
    /// PP742: EIGHTEEN BECAME TWENTY-EIGHT AND THE DECISION IS THE SAME ONE. Twenty-eight is where
    /// the longest AV header ends, so the keep still stops at the end of a header and every byte
    /// past it is still content. What moved is which header - the MAC gate's furthest read, or the
    /// whole AV head the port's own parser wants before it reads any of it.
    ///
    /// Asserted against the constant rather than the number, so this says "still a head" instead of
    /// "still eighteen". A capture that grew to WholeDatagramBytes is what it is here to catch, and
    /// that is a different number by two orders of magnitude.
    /// </summary>
    [Fact]
    public void TheCorpusStillKeepsHeads()
    {
        Assert.Equal(TakionTimingCapture.HeadBytes, DatagramCorpus.HeadBytes);
        Assert.True(
            DatagramCorpus.HeadBytes < TakionTimingCapture.WholeDatagramBytes / 10,
            $"the corpus keeps {DatagramCorpus.HeadBytes} bytes, which is no longer a head");

        Assert.Equal(WholeDatagramReplay.Datagrams, DatagramCorpus.Datagrams);
    }
}
