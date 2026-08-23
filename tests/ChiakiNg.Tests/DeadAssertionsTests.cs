using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP318: the shape no analyzer names.
///
/// <see cref="NoAssertionInThisTreeComparesTwoConstants"/> carries the task. Without the deletion in
/// NatDiagnosisTests it is red, and it was red for as long as that line stood - which was every run
/// since it was written, in a suite reporting green.
///
/// Every sample below lives inside a string literal, which is exactly what the scanner blanks. That
/// is the point rather than a trick: PP317's check went red on its own project's prose, and a check
/// that could not describe itself would need an exemption list somebody has to maintain.
/// </summary>
public class DeadAssertionsTests(ITestOutputHelper output)
{
    /// <summary>THE TASK. Nothing in the assertion corpus compares two constants.</summary>
    [Fact]
    public void NoAssertionInThisTreeComparesTwoConstants()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        IReadOnlyList<DeadAssertions.DeadAssertion> dead = DeadAssertions.Sweep(root);
        foreach (DeadAssertions.DeadAssertion one in dead)
            output.WriteLine(one.ToString());

        Assert.True(
            dead.Count == 0,
            "these assertions compare constants written in the test itself, so they pass whatever "
                + "the code does: " + string.Join("; ", dead));
    }

    /// <summary>The corpus is not empty, or the sweep above proves nothing by finding nothing.</summary>
    [Fact]
    public void TheSweepReadsFilesAtAll()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        Assert.NotEmpty(AssertionRatchet.AssertionFiles(root));
    }

    /// <summary>The shape PP316 found, and the three beside it.</summary>
    [Theory]
    [InlineData("Assert.Equal(0, 0);")]
    [InlineData("Assert.Equal(4, 4);")]
    [InlineData("Assert.NotEqual(1, 2);")]
    [InlineData("Assert.True(true);")]
    [InlineData("Assert.False(false);")]
    [InlineData("""Assert.Equal("a", "a");""")]
    [InlineData("Assert.Equal('-', '-');")]
    [InlineData("""Assert.True(true, "the message does not save it");""")]
    public void AComparisonOfTwoConstantsIsFound(string line)
        => Assert.Single(DeadAssertions.In(line));

    /// <summary>And an assertion with a subject is not one, however many literals are beside it.</summary>
    [Theory]
    [InlineData("Assert.Equal(36, uuid.Length);")]
    [InlineData("Assert.Equal(4, uuid.Count(c => c == '-'));")]
    [InlineData("""Assert.Equal("00ff1a", HolepunchIdentifiers.BytesToHex(bytes));""")]
    [InlineData("Assert.True(written.Writes);")]
    [InlineData("Assert.False(NatDiagnosis.WriteBackFor(verdict).Writes);")]
    [InlineData("Assert.Equal(0, NatDiagnosis.OverrunFor(46));")]
    public void AnAssertionWithASubjectIsLeftAlone(string line)
        => Assert.Empty(DeadAssertions.In(line));

    /// <summary>
    /// PP317's lesson, applied before it could bite: a sample in a comment is not a call.
    /// </summary>
    [Fact]
    public void WhatACommentSaysIsNotWhatTheCodeDoes()
    {
        Assert.Empty(DeadAssertions.In("// an Assert.Equal(0, 0) stood here once"));
        Assert.Empty(DeadAssertions.In("/// <summary>Assert.True(true) is the shape.</summary>"));
        Assert.Empty(DeadAssertions.In("/* Assert.Equal(0, 0);\n   and still commented */"));
    }

    /// <summary>And a sample inside a string is not one either, in all four spellings.</summary>
    [Fact]
    public void WhatAStringHoldsIsNotACall()
    {
        Assert.Empty(DeadAssertions.In("""var sample = "Assert.Equal(0, 0);";"""));
        Assert.Empty(DeadAssertions.In("""var sample = @"Assert.Equal(0, 0);";"""));
        Assert.Empty(DeadAssertions.In("var sample = \"\"\"Assert.Equal(0, 0);\"\"\";"));
        Assert.Empty(DeadAssertions.In("var sample = \"\"\"\nAssert.True(true);\n\"\"\";"));
    }

    /// <summary>
    /// A line number still means what it said. Blanking a string that spans lines has to keep the
    /// newlines, or the report sends a reader to the wrong line - which is worse than no report.
    /// </summary>
    [Fact]
    public void TheLineNumberSurvivesWhatWasBlanked()
    {
        string source =
            "var banner = \"\"\"\nline two\nline three\n\"\"\";\nAssert.Equal(0, 0);\n";

        DeadAssertions.DeadAssertion one = Assert.Single(DeadAssertions.In(source, "sample.cs"));

        Assert.Equal(5, one.Line);
        Assert.Equal("sample.cs", one.File);
    }

    /// <summary>A verbatim string's doubled quote does not end it, so what follows stays inside.</summary>
    [Fact]
    public void ADoubledQuoteDoesNotEndAVerbatimString()
        => Assert.Empty(DeadAssertions.In("""var s = @"he said ""hi"" then Assert.Equal(0, 0);";"""));

    /// <summary>An escaped quote does not end an ordinary string either.</summary>
    [Fact]
    public void AnEscapedQuoteDoesNotEndAString()
        => Assert.Empty(DeadAssertions.In("""var s = "he said \"hi\" then Assert.Equal(0, 0);";"""));
}
