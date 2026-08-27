using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP428: the error codes the shipped build ignores entirely.
///
/// PP426 counted the build's warnings and found seventeen saying what PP357 argued from the source,
/// PP404 censused and PP406 refined - and none of the three had cited the compiler. This is the
/// join, derived rather than transcribed.
/// </summary>
public class UnreadErrorCodesTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE TWO READINGS AGREE. What this finds is what the compiler prints.
    ///
    /// The count is the whole point: it was derived from the shape - a ChiakiErrorCode declaration
    /// whose identifier appears nowhere but inside an assert - and it is held against a number that
    /// came from a different program entirely. PP425 established why that matters: a list copied out
    /// of the compiler's output would only confirm the copy.
    /// </summary>
    [Fact]
    public void TheCountIsTheOneTheCompilerPrints()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<UnreadErrorCode> found = UnreadErrorCodes.All(root);

        foreach (UnreadErrorCode one in found)
            output.WriteLine($"{one.File}  {one.Variable} = {one.Callee}(..)");

        Assert.Equal(UnreadErrorCodes.WhatTheCompilerCounts, found.Count);
    }

    /// <summary>
    /// AND EVERY ONE IS IN PP404'S CENSUS, which is the hole this closes.
    ///
    /// The census is authoritative about what is asserted and is a superset of these. A site the
    /// compiler names that the census misses would be a gap in its grep, invisible because the
    /// census would still read as complete.
    /// </summary>
    [Fact]
    public void EveryOneIsAlsoInTheAssertedCensus()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;
        if (AssertedErrorCodes.Locate() is not { } directory)
            return;

        IReadOnlyDictionary<string, IReadOnlyList<string>> census =
            AssertedErrorCodes.Census(directory);

        var missing = new List<string>();

        foreach (IGrouping<string, UnreadErrorCode> file in
                 UnreadErrorCodes.All(root).GroupBy(one => one.File))
        {
            // The census keys by path; match on the file name, which is unique in these eight.
            string name = Path.GetFileName(file.Key);

            IReadOnlyList<string>? asserted = census
                .Where(entry => Path.GetFileName(entry.Key) == name)
                .Select(entry => entry.Value)
                .FirstOrDefault();

            if (asserted is null || asserted.Count < file.Count())
                missing.Add($"{file.Key}: {file.Count()} unread, census has {asserted?.Count ?? 0}");
        }

        Assert.True(
            missing.Count == 0,
            "the compiler names error codes the asserted census does not hold, so the census has a "
                + "hole that reads as completeness:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// SIXTEEN OF THE SEVENTEEN CANNOT FAIL, and the seventeenth is worth having counted.
    ///
    /// PP406 established that a mutex lock and a condition signal have no failure path on this
    /// platform. rpcrypt.c's bright_ambassador does have one - it refuses a target below PS4_10 -
    /// and it is unreachable, because session->target is initialised to PS4_10 or PS5_1 and is only
    /// reassigned from a server target that chiaki_target_is_unknown has already rejected. Counted
    /// rather than assumed, which is the difference between this and a shrug.
    /// </summary>
    [Fact]
    public void OnlyOneOfThemAssertsSomethingThatCanFail()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<UnreadErrorCode> found = UnreadErrorCodes.All(root);

        IReadOnlyList<UnreadErrorCode> locksAndSignals =
            [.. found.Where(one =>
                one.Callee is "chiaki_mutex_lock" or "chiaki_cond_signal")];

        Assert.Equal(found.Count - 1, locksAndSignals.Count);

        UnreadErrorCode other = Assert.Single(found.Except(locksAndSignals));
        Assert.Equal("bright_ambassador", other.Callee);
        Assert.EndsWith("rpcrypt.c", other.File, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shape, on text rather than on the tree.
    ///
    /// An error code read only by an assert is reported; one read anywhere else is not, because the
    /// assert going does not leave it unread.
    /// </summary>
    [Fact]
    public void OnlyAnAssertOnlyReaderIsReported()
    {
        const string OnlyAsserted = """
            void f(void) {
            	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
            	assert(err == CHIAKI_ERR_SUCCESS);
            	ctrl->should_stop = true;
            }
            """;

        UnreadErrorCode one = Assert.Single(UnreadErrorCodes.InFile(OnlyAsserted));
        Assert.Equal("err", one.Variable);
        Assert.Equal("chiaki_mutex_lock", one.Callee);

        // Read again afterwards: the assert is not its only reader.
        const string AlsoReturned = """
            void f(void) {
            	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
            	assert(err == CHIAKI_ERR_SUCCESS);
            	return err;
            }
            """;

        Assert.Empty(UnreadErrorCodes.InFile(AlsoReturned));

        // Checked instead of asserted: also not reported.
        const string Checked = """
            void f(void) {
            	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
            	if(err != CHIAKI_ERR_SUCCESS)
            		return err;
            }
            """;

        Assert.Empty(UnreadErrorCodes.InFile(Checked));
    }

    /// <summary>
    /// A REASSIGNMENT IS NOT A READ, which is what the compiler's count caught in this reader.
    ///
    /// chiaki_takion_send_buffer_fini asserts a cond signal, then reassigns err from a thread join
    /// and asserts that too. Counting the reassignment as a read left it and two others out - 14 of
    /// 17 - and the compiler calls those "set but not used" precisely because BOTH assignments are
    /// only ever asserted. Having an independent number is what turned a plausible reader into a
    /// correct one.
    /// </summary>
    [Fact]
    public void AReassignmentIsNotARead()
    {
        const string Reassigned = """
            void f(void) {
            	ChiakiErrorCode err = chiaki_cond_signal(&send_buffer->cond);
            	assert(err == CHIAKI_ERR_SUCCESS);
            	err = chiaki_thread_join(&send_buffer->thread, NULL);
            	assert(err == CHIAKI_ERR_SUCCESS);
            }
            """;

        UnreadErrorCode one = Assert.Single(UnreadErrorCodes.InFile(Reassigned));
        Assert.Equal("chiaki_cond_signal", one.Callee);

        // And the write/read distinction on its own.
        Assert.True(UnreadErrorCodes.IsWrittenAt("err = f();", 3));
        Assert.False(UnreadErrorCodes.IsWrittenAt("err == CHIAKI_ERR_SUCCESS", 3));
        Assert.False(UnreadErrorCodes.IsWrittenAt("err != CHIAKI_ERR_SUCCESS", 3));
        Assert.False(UnreadErrorCodes.IsWrittenAt("return err;", 10));
    }

    /// <summary>
    /// A declaration nothing reads at all is not reported either.
    ///
    /// The compiler calls that set-but-not-used too, and it is a different fact: nothing about it is
    /// an error code an assert was standing in for.
    /// </summary>
    [Fact]
    public void ADeclarationWithNoReaderIsNotAnAssertedOne()
    {
        const string Unread = "\tChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);\n";

        Assert.Empty(UnreadErrorCodes.InFile(Unread));
    }

    /// <summary>And a comment naming the shape does not produce one - PP400's rule.</summary>
    [Fact]
    public void ACommentDoesNotCount()
    {
        const string Commented = """
            	// ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
            	// assert(err == CHIAKI_ERR_SUCCESS);
            """;

        Assert.Empty(UnreadErrorCodes.InFile(Commented));
    }

    /// <summary>PP272: and an empty tree yields nothing rather than a pass about nothing.</summary>
    [Fact]
    public void AnEmptyTreeYieldsNothing()
    {
        Assert.Empty(UnreadErrorCodes.InFile(""));
        Assert.False(UnreadErrorCodes.InsideAnAssert("", 0));
        Assert.False(UnreadErrorCodes.OnlyReaderIsAnAssert("", "err", 0));
        Assert.Equal((-1, -1), UnreadErrorCodes.EnclosingBlock("", 0));
    }
}
