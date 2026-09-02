using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP621: PP33's deletion is sized at two C callers, and the oracle it breaks is much larger.
///
/// PP573 holds PP33's line to the count in <see cref="HolepunchConsumers.All"/>, and PP584 holds
/// every deletion line to naming this port's own shim. Both are about translation units - what the
/// linker would break. Neither can see that session.c's holepunch text is quoted as a SPECIFICATION
/// by the models this port was ported into, and asserted against by the files here.
///
/// So these are the assertion the finding needs: the cost is bigger than the line says, and it is
/// bigger by an amount the tree answers rather than a number somebody typed.
/// </summary>
public class HolepunchOracleReadersTests
{
    /// <summary>A file the census must find, chosen because its whole subject is the handle.</summary>
    private const string DirectionModel = @"app\Protocol\HolepunchDirection.cs";

    /// <summary>And one whose subject is something else, which quotes it all the same.</summary>
    private const string ReleaseModel = @"app\Protocol\SessionRelease.cs";

    /// <summary>This census itself, which must not be in its own answer.</summary>
    private const string TheCensus = @"app\Protocol\HolepunchOracleReaders.cs";

    /// <summary>
    /// PP621: the deletion's real size, against the size PP33's line states.
    ///
    /// THE ASSERTION THE TASK IS FOR. PP33's line says two files call holepunch.c and that is true;
    /// what it leaves out is that removing the nine calls from one of those two invalidates every
    /// file counted here. A strict inequality rather than a number: the census moves as models are
    /// converted, and the claim that must not stop being true is that a caller count understates the
    /// work - which is what a session reads the line to decide.
    /// </summary>
    [Fact]
    public void TheOracleIsLargerThanTheCallerList()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<OracleReader> census = HolepunchOracleReaders.Census(root);

        Assert.True(
            census.Count > HolepunchConsumers.All.Count,
            $"the handle is quoted by {census.Count} file(s) and PP33's line is sized at "
                + $"{HolepunchConsumers.All.Count} caller(s)");
    }

    /// <summary>
    /// PP621: and both halves of the port are in it, which is what makes it an oracle rewrite.
    ///
    /// A model alone could be edited with the C in one pass. What costs is that each model has tests
    /// asserting the text it holds, so the deletion is two files per fact and not one.
    /// </summary>
    [Fact]
    public void BothHalvesOfThePortQuoteTheHandle()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<OracleReader> census = HolepunchOracleReaders.Census(root);

        Assert.Contains(census, one => !one.IsTest);
        Assert.Contains(census, one => one.IsTest);
    }

    /// <summary>
    /// PP621: the census finds the models a reader would name first, and one they would not.
    ///
    /// HolepunchDirection is the obvious member - its whole subject is where the handle is read.
    /// SessionRelease is the one that makes the point: its subject is teardown order, and it quotes
    /// the handle because session.c's fini sites are part of that order. A hand-written list of "the
    /// holepunch models" would have the first and could miss the second.
    /// </summary>
    [Fact]
    public void TheModelsThatQuoteItAreFound()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<string> paths =
            [.. HolepunchOracleReaders.Census(root).Select(one => one.RelativePath)];

        Assert.Contains(DirectionModel, paths);
        Assert.Contains(ReleaseModel, paths);
    }

    /// <summary>
    /// PP621: and the census is not in its own answer, which is why the handle is named once.
    ///
    /// <see cref="HolepunchOracleReaders.Handle"/> reads <see cref="HolepunchDirection.Handle"/>
    /// rather than spelling the text again. Spelling it would make this file a reader of session.c,
    /// and the count would then include the thing doing the counting.
    /// </summary>
    [Fact]
    public void TheCensusIsNotItsOwnMember()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        Assert.DoesNotContain(
            TheCensus,
            HolepunchOracleReaders.Census(root).Select(one => one.RelativePath));
    }

    /// <summary>
    /// PP621: build output is not counted, so the answer does not depend on how often somebody built.
    ///
    /// app\bin and app\obj hold a copy of the models under every configuration and target framework
    /// this host has been built for. A census that walked them would rise on a rebuild and fall on a
    /// clean, which is a number about the machine rather than about the tree.
    /// </summary>
    [Fact]
    public void BuildOutputIsNotCounted()
    {
        Assert.True(HolepunchOracleReaders.IsGenerated(@"app\obj\Debug\net10.0-windows\x.cs"));
        Assert.True(HolepunchOracleReaders.IsGenerated(@"app/bin/Release/x.cs"));
        Assert.False(HolepunchOracleReaders.IsGenerated(DirectionModel));
    }

    /// <summary>
    /// PP621: and a file that does not quote the handle is not a reader, however much holepunch it
    /// talks about.
    ///
    /// The census is keyed on session.c's own text and not on the word. Most of app/Protocol is about
    /// the holepunch protocol and models the managed side of it; what this counts is the far smaller
    /// set that would have to change because a line of session.c did.
    /// </summary>
    [Fact]
    public void OnlyTheHandleMakesAReader()
    {
        Assert.False(HolepunchOracleReaders.Quotes("chiaki_holepunch_session_punch_hole(handle, 0);"));
        Assert.True(HolepunchOracleReaders.Quotes(
            "if(" + HolepunchOracleReaders.Handle + ")"));
    }

    /// <summary>
    /// PP621: and the tests that assert against those models outnumber the models themselves.
    ///
    /// THE HALF A TEXT SEARCH CANNOT SEE. Only three files under tests/ quote the handle, which
    /// reads as though converting the models is nearly the whole job. A model states what session.c
    /// does and its tests assert that statement, so they carry the fact without carrying the text -
    /// and they are the larger half. Asserted as an inequality against the models, because both
    /// numbers move as the conversion lands and the claim that must survive is which is bigger.
    /// </summary>
    [Fact]
    public void TheTestsAssertingAgainstThemAreTheLargerHalf()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<OracleReader> census = HolepunchOracleReaders.Census(root);
        IReadOnlyList<string> dependents = HolepunchOracleReaders.Dependents(root, census);

        Assert.True(
            dependents.Count > census.Count(one => !one.IsTest),
            $"{dependents.Count} test file(s) assert against {census.Count(one => !one.IsTest)} model(s)");

        Assert.DoesNotContain(HolepunchOracleReaders.CensusRelativePath, dependents);
    }

    /// <summary>
    /// PP621: and a census with no models has no dependents, which is what a finished conversion
    /// looks like from here.
    ///
    /// The end state this task is measuring towards: nothing quotes session.c's handle, so nothing
    /// asserts against something that does, and the deletion owes the oracle nothing.
    /// </summary>
    [Fact]
    public void AnEmptyCensusHasNoDependents()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        Assert.Empty(HolepunchOracleReaders.Dependents(root, []));
    }

    /// <summary>
    /// PP621: and §PP33 says so, which is where a session picking the line would read it.
    ///
    /// The models are where a finding lands and the LINE is somewhere else - PP573's whole subject,
    /// which is why PP33's reason went on saying "only caller" through three findings. A census
    /// nothing points at repeats that: it would be correct, and read by nobody deciding what the
    /// deletion costs.
    /// </summary>
    [Fact]
    public void ThePP33SectionPointsAtTheCensus()
    {
        if (SanitizerSource.LocateRelative(@"docs\IMPROVEMENTS.md") is not { } path)
            return;

        string improvements = File.ReadAllText(path);
        int start = improvements.IndexOf("### §PP33 ", StringComparison.Ordinal);
        if (start < 0)
            return;

        int end = improvements.IndexOf("\n### ", start, StringComparison.Ordinal);
        string section = end < 0 ? improvements[start..] : improvements[start..end];

        Assert.Contains("PP621", section, StringComparison.Ordinal);
    }
}
