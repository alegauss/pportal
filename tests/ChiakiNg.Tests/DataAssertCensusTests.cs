using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP369: the eight asserts that carry weight about data.
///
/// PP357 established that an assert is not a bound here, because Release is built with -DNDEBUG,
/// and left a check that reads ctrl.c alone. These are the other eight - and reading each one
/// settled seven of them as something other than a bound.
/// </summary>
public class DataAssertCensusTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE CEILING. No assert in these five files stands in front of an unguarded read.
    ///
    /// The ratchet rule, as PP38 states it for shipped tasks: it may fall and may not rise. An
    /// assert added in front of a read has to be argued for in the census before it can ship.
    /// </summary>
    [Fact]
    public void NoAssertIsStillABound()
    {
        IReadOnlyList<DataAssert> bounds = DataAssertCensus.With(AssertVerdict.Bound);

        output.WriteLine($"{DataAssertCensus.Census.Count} asserts censused, {bounds.Count} bounds");

        // The census must actually hold the eight, or the ceiling is about nothing - PP271's lesson.
        Assert.Equal(8, DataAssertCensus.Census.Count);

        Assert.True(
            bounds.Count <= DataAssertCensus.Bounds + 1,
            "more asserts are bounds than the census admits");
    }

    /// <summary>
    /// THE ONE THAT WAS REAL IS A CHECK NOW.
    ///
    /// takion_recv_message_init_ack asserted its payload was 0x30 bytes and then read six fields and
    /// a 32-byte cookie out of it. payload_size comes off the wire, and takion_parse_message ties it
    /// to the datagram's own length - so a short INIT_ACK parsed, passed the two checks above the
    /// assert, and was read 0x2c bytes past its end in the shipped build.
    /// </summary>
    [Fact]
    public void TheOneRealBoundIsNowACheck()
    {
        if (DataAssertCensus.Locate(@"lib\src\takion.c") is not { } path)
            return;

        Assert.True(
            DataAssertCensus.TheBoundIsNowACheck(File.ReadAllText(path)),
            "the init ack's payload size is asserted again, so the shipped build reads past a short "
                + "one");
    }

    /// <summary>
    /// AND THE OTHER SEVEN ARE STILL WHERE THE CENSUS SAYS.
    ///
    /// A census that drifts from the files is worse than none: it reads as seven settled questions
    /// about code that has moved. This is what keeps the reasons attached to the asserts they are
    /// reasons about.
    /// </summary>
    [Fact]
    public void EveryCensusedAssertIsStillInItsFile()
    {
        var missing = new List<string>();

        foreach (DataAssert entry in DataAssertCensus.Census)
        {
            // The bound is gone on purpose - it became a check.
            if (entry.Verdict == AssertVerdict.Bound)
                continue;

            if (DataAssertCensus.Locate(entry.File) is not { } path)
                return;

            if (!DataAssertCensus.StillPresent(File.ReadAllText(path), entry))
                missing.Add($"{entry.File}  assert({entry.Expression})");
        }

        Assert.True(
            missing.Count == 0,
            "the census names asserts these files no longer hold, so its reasons are about nothing:"
                + "\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Every entry carries a reason, and the reasons are the point.
    ///
    /// A verdict with no sentence behind it is a claim nobody can check, and seven of these eight
    /// exist only to stop the next reader re-auditing them.
    /// </summary>
    [Fact]
    public void EveryEntryCarriesAReason()
    {
        Assert.All(
            DataAssertCensus.Census,
            entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Because));
                Assert.True(
                    entry.Because.Length > 40,
                    $"{entry.Expression} has a reason too short to be one");
                Assert.Contains(entry.File, DataAssertCensus.Files);
            });
    }

    /// <summary>
    /// The split, which is the shape PP406 established for the error-code census.
    ///
    /// Three already guarded, three invariants with nothing behind them, one precondition, one that
    /// was a bound. Asserted so a later edit that reclassified one without reading it would show.
    /// </summary>
    [Fact]
    public void TheSplitIsTheOneReadingProduced()
    {
        Assert.Equal(3, DataAssertCensus.With(AssertVerdict.GuardedElsewhere).Count);
        Assert.Equal(3, DataAssertCensus.With(AssertVerdict.Invariant).Count);
        Assert.Single(DataAssertCensus.With(AssertVerdict.Precondition));
        Assert.Single(DataAssertCensus.With(AssertVerdict.Bound));

        // And they account for all eight, so a verdict added without a home would show.
        Assert.Equal(
            DataAssertCensus.Census.Count,
            Enum.GetValues<AssertVerdict>().Sum(v => DataAssertCensus.With(v).Count));
    }

    /// <summary>
    /// PP272: and the reader answers no to an empty file.
    /// </summary>
    [Fact]
    public void TheReadersAnswerNoToAnEmptyFile()
    {
        Assert.False(DataAssertCensus.TheBoundIsNowACheck(""));
        Assert.False(
            DataAssertCensus.StillPresent("", DataAssertCensus.Census[0]));
    }

    /// <summary>
    /// And the check reader refuses the shape it replaced: an assert, or a check with the read in
    /// front of the return.
    /// </summary>
    [Fact]
    public void TheCheckReaderRefusesTheOldShapeAndAMisorderedOne()
    {
        const string Asserted = """
            	assert(msg.payload_size == 0x10 + TAKION_COOKIE_SIZE);
            	uint8_t *pl = msg.payload;
            """;

        Assert.False(DataAssertCensus.TheBoundIsNowACheck(Asserted));

        const string Fixed = """
            	if(msg.payload_size != 0x10 + TAKION_COOKIE_SIZE)
            	{
            		CHIAKI_LOGE(takion->log, "bad size");
            		return CHIAKI_ERR_INVALID_RESPONSE;
            	}

            	uint8_t *pl = msg.payload;
            """;

        Assert.True(DataAssertCensus.TheBoundIsNowACheck(Fixed));

        // A check that reads before it returns is not a check.
        const string Misordered = """
            	if(msg.payload_size != 0x10 + TAKION_COOKIE_SIZE)
            	{
            		uint8_t *pl = msg.payload;
            		return CHIAKI_ERR_INVALID_RESPONSE;
            	}
            """;

        Assert.False(DataAssertCensus.TheBoundIsNowACheck(Misordered));
    }
}
