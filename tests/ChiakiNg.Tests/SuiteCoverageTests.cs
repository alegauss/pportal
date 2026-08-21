using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: every file in the C suite is accounted for on this side, or says why not.
///
/// PP23's line said "the rest of the modules" and nobody could tell which. Finding out took an
/// audit - walk test/, list what each file covers, and see what the port never carried - and that
/// audit found two real gaps in a row: the allocation budget (PP176) and the baseline percentile
/// (PP177), which led to four more.
///
/// An audit run by hand is an audit that is right on the day it is run. This is the same walk as a
/// check: every .c in test/ has a row here, and a new one that appears has none - so the next case
/// the other client adds is a red test rather than a thing nobody noticed for a year.
///
/// A row saying NOT PORTED is a real answer, not a hole. Two of them are, and both name what is
/// missing rather than promising it: a decoder this port does not have, and a test harness that
/// belongs to the C build.
/// </summary>
public class SuiteCoverageTests
{
    /// <summary>How one file of the C suite is answered on this side.</summary>
    /// <param name="File">The file in test/.</param>
    /// <param name="CoveredBy">Where the answer lives, or why there is none.</param>
    private sealed record Coverage(string File, string CoveredBy);

    private static readonly Coverage[] Rows =
    [
        new("allocbudget.c", "AllocBudgetTests - PP176"),
        new("bitstream.c", "BitstreamParserOracleTests, BitstreamRewriteOracleTests, BitstreamTruncationTests"),
        new("decoderchoice.c", "VideoSettingsTests - the choice is a settings table here"),
        new("fec.c", "FecVectorTests"),
        new("frameprocessor.c", "AllocBudgetTests and the host selftest"),
        new("gkcrypt.c", "GmacVectorTests"),
        new("http.c", "HttpDifferentialTests - PP184"),
        new("keystate.c", "SeqNumTests and the host selftest"),
        new("regist.c", "RpCryptRegistTests, RegistrationFlowTests"),
        new("reorderqueue.c", "ReorderQueueOracleTests, ReorderQueueTests"),
        new("rpcrypt.c", "RpCryptVectorTests"),
        new("seqnum.c", "SeqNumTests"),
        new("sessionbaseline.c", "BaselineStatTests, BaselineLineTests, BaselineFieldSetTests, "
            + "BaselineStageTests, BaselineDefaultsTests, BaselineAppendTests - PP177 to PP182"),
        new("takion.c", "SendBufferTests, CongestionTests and the host selftest"),
        new("videoreceiver.c", "VideoStreamTests"),

        // The two that are not ported, each saying what is missing rather than promising it.
        new("ffmpegdecoder.c", "NOT PORTED: this port has no decoder yet - the frame path stops at "
            + "the frame processor, and a decoder arrives with the renderer chain"),
        new("main.c", "NOT PORTED: munit's entry point, which belongs to the C build"),
        new("test_log.c", "NOT PORTED: munit's log sink, likewise"),
    ];

    private static string? SuiteRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "test");
            if (File.Exists(Path.Combine(candidate, "main.c")))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Every .c in the C suite has a row, and every row names a file that is still there. Both
    /// directions, because a file added and a file removed are different mistakes and this table
    /// would go quietly stale on either.
    /// </summary>
    [Fact]
    public void EveryFileInTheCSuiteIsAccountedFor()
    {
        string? suite = SuiteRoot();
        if (suite is null)
            return;

        string[] onDisk = [.. Directory
            .EnumerateFiles(suite, "*.c")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .Order(StringComparer.Ordinal)];

        string[] tabled = [.. Rows.Select(r => r.File).Order(StringComparer.Ordinal)];

        string[] unaccounted = [.. onDisk.Except(tabled, StringComparer.Ordinal)];
        string[] vanished = [.. tabled.Except(onDisk, StringComparer.Ordinal)];

        Assert.True(
            unaccounted.Length == 0,
            "the C suite has files this port does not answer for: " + string.Join(", ", unaccounted));

        Assert.True(
            vanished.Length == 0,
            "this table names files the C suite no longer has: " + string.Join(", ", vanished));
    }

    /// <summary>
    /// The rows that say NOT PORTED say WHY. A row that only said no would be a hole wearing a
    /// table's clothes, and the point of the table is that the holes are named.
    /// </summary>
    [Fact]
    public void EveryGapNamesWhatIsMissing()
    {
        foreach (Coverage row in Rows.Where(r => r.CoveredBy.StartsWith("NOT PORTED", StringComparison.Ordinal)))
        {
            Assert.Contains(':', row.CoveredBy);
            Assert.True(
                row.CoveredBy.Length > "NOT PORTED: ".Length + 20,
                $"{row.File} says it is not ported and does not say what is missing");
        }
    }

    /// <summary>
    /// And every test file a row names is really in this project, so the table cannot claim
    /// coverage from a file somebody deleted.
    /// </summary>
    [Fact]
    public void EveryNamedTestFileExists()
    {
        string? suite = SuiteRoot();
        if (suite is null)
            return;

        string tests = Path.Combine(Path.GetDirectoryName(suite)!, "tests", "ChiakiNg.Tests");
        if (!Directory.Exists(tests))
            return;

        var missing = new List<string>();

        foreach (Coverage row in Rows)
        {
            if (row.CoveredBy.StartsWith("NOT PORTED", StringComparison.Ordinal))
                continue;

            foreach (string name in row.CoveredBy.Split(',', StringSplitOptions.TrimEntries))
            {
                // Only the entries that name a test class; the prose after a dash is not one.
                if (!name.EndsWith("Tests", StringComparison.Ordinal))
                    continue;

                if (!File.Exists(Path.Combine(tests, name + ".cs")))
                    missing.Add($"{row.File} names {name}, which is not in this project");
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }
}
