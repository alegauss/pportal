using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP584: a deletion line's caller claim names the shim, because the shim calls everything.
/// </summary>
public class DeletionCallerClaimsTests
{
    private static string? Roadmap()
        => DeletionCallerClaims.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// EVERY DELETION LINE NAMES THE SEAM, and three in a row did not.
    ///
    /// PP33's line said session.c was the only caller of holepunch and there were four. PP30's said
    /// the FEC decode had one caller left and there were three. PP295's said streamconnection.c was
    /// the last C caller of the video receiver, and the shim wraps five of its exports - one of
    /// which streamconnection does not use.
    ///
    /// The same omission each time, and PP574 counted why: the shim wraps 130 entry points across
    /// every module, so it calls everything a deletion removes.
    /// </summary>
    [Fact]
    public void EveryDeletionLineNamesTheSeam()
    {
        if (Roadmap() is not { } roadmap)
            return;

        IReadOnlyList<string> silent = DeletionCallerClaims.NotNamingTheSeam(roadmap);
        Assert.True(silent.Count == 0, $"deletion lines not naming the shim: {string.Join(", ", silent)}");
    }

    /// <summary>
    /// And the lines are actually being read. All three are open today; a check that found none
    /// would pass the test above while saying nothing.
    /// </summary>
    [Fact]
    public void TheLinesAreThereToRead()
    {
        if (Roadmap() is not { } roadmap)
            return;

        foreach (DeletionLine line in DeletionCallerClaims.All)
            Assert.NotNull(DeletionCallerClaims.LineFor(roadmap, line.Id));
    }

    /// <summary>A line that omits the seam is named, with what it deletes, not merely counted.</summary>
    [Fact]
    public void ALineOmittingTheSeamIsNamed()
    {
        // PP33 was the silent one here until it shipped, and its claim - "session.c is its only
        // caller" - is why this rule exists at all. The fixture moves to a line still open rather
        // than the demonstration going with it.
        // PP295 has shipped too, and the fixture moved again for the same reason: it demonstrates
        // the rule on a line that is still open, because a row about a shipped line is a row this
        // check no longer has.
        const string roadmap = """
            - 📋 **PP30** (deps: —) **something** — fec.c is its only caller. → §PP30
            """;

        IReadOnlyList<string> silent = DeletionCallerClaims.NotNamingTheSeam(roadmap);

        Assert.Single(silent);
        Assert.Contains("PP30", silent[0], StringComparison.Ordinal);
        Assert.Contains("the FEC decode", silent[0], StringComparison.Ordinal);

        // And a line that DOES name the seam is not reported, which is what makes this a rule.
        Assert.Empty(DeletionCallerClaims.NotNamingTheSeam(
            "- 📋 **PP30** (deps: —) **something** — and the shim is one of them. → §PP30"));
    }

    /// <summary>
    /// A line that has shipped and left the roadmap is not a failure: it is answered by no longer
    /// being open, and this reports only what it can read.
    /// </summary>
    [Fact]
    public void AShippedLineIsNotAFailure()
        => Assert.Empty(DeletionCallerClaims.NotNamingTheSeam("# a roadmap with none of them\n"));
}
