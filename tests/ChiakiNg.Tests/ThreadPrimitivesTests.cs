using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP406: PP404 counted 53 asserted error codes and did not ask which callee can fail.
///
/// On Windows most of them cannot. A ceiling that is mostly unreachable branches invites work with
/// no effect and hides the ones that would have some.
/// </summary>
public class ThreadPrimitivesTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE TASK. The count that matters is the one whose callee has a failure path.
    /// </summary>
    [Fact]
    public void OnlyTheCalleesThatCanFailAreCounted()
    {
        string? directory = AssertedErrorCodes.Locate();
        IReadOnlyDictionary<string, string>? primitives = ThreadPrimitives.Read();
        if (directory is null || primitives is null)
            return;

        IReadOnlyDictionary<string, IReadOnlyList<string>> risky =
            AssertedErrorCodes.CanFail(directory, primitives);
        int total = risky.Values.Sum(a => a.Count);

        foreach ((string file, IReadOnlyList<string> asserts) in risky.OrderByDescending(e => e.Value.Count))
        {
            foreach (string assert in asserts)
                output.WriteLine($"{file,-22} {assert}");
        }

        // PP271: a classifier that stopped identifying callees would report zero and read as a
        // tree with nothing left to look at.
        Assert.NotEmpty(risky);

        Assert.Equal(AssertedErrorCodes.CanFailCeiling, total);

        // And it is a real subset - the whole point is that most of the census is not in it.
        Assert.True(
            total < AssertedErrorCodes.Ceiling,
            $"{total} of {AssertedErrorCodes.Ceiling} can fail, which is not a split at all");
    }

    /// <summary>
    /// The lock cannot fail, and this is read out of thread.c rather than remembered.
    ///
    /// <c>chiaki_mutex_lock</c> is EnterCriticalSection and one return of the success constant. If
    /// that ever grows a failure path, its twenty-four call sites join the count above on their own.
    /// </summary>
    [Theory]
    [InlineData("chiaki_mutex_lock", false)]
    [InlineData("chiaki_mutex_init", false)]
    [InlineData("chiaki_mutex_unlock", false)]
    [InlineData("chiaki_cond_init", false)]
    [InlineData("chiaki_cond_signal", false)]
    // The neatest contrast is missing on purpose. The try-variant of the lock CAN fail and is a
    // few letters from the one that cannot - but PP290 records it as an export nothing in the tree
    // refers to, and writing its name here, in a comment or in a row, is a reference: the sweep
    // matches bare identifiers. It would leave that record saying the port had started using a
    // function it has not. The synthetic pair below makes the same point owing nobody anything.
    [InlineData("chiaki_cond_timedwait_pred", true)]
    [InlineData("chiaki_stop_pipe_init", true)]
    public void EachPrimitiveIsJudgedByItsOwnReturns(string name, bool canFail)
    {
        IReadOnlyDictionary<string, string>? primitives = ThreadPrimitives.Read();
        if (primitives is null)
            return;

        Assert.Equal(canFail, ThreadPrimitives.CanFail(name, primitives));
    }

    /// <summary>
    /// PP272: and a primitive nothing defines is reported as able to fail, not as safe.
    ///
    /// The safe direction for an unreadable definition is to widen what gets looked at.
    /// </summary>
    [Fact]
    public void AnUnknownPrimitiveCanFail()
    {
        Assert.True(ThreadPrimitives.CanFail("chiaki_nothing_defines_this", ""));
        Assert.True(ThreadPrimitives.CanFail("chiaki_mutex_lock", ""));

        // The reader itself: one success return is not a failure path, and any other return is.
        Assert.False(ThreadPrimitives.CanFail(
            "chiaki_x", "CHIAKI_EXPORT ChiakiErrorCode chiaki_x(void)\n{\n\treturn CHIAKI_ERR_SUCCESS;\n}\n"));
        Assert.True(ThreadPrimitives.CanFail(
            "chiaki_x",
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_x(void)\n{\n\tif(a)\n\t\treturn CHIAKI_ERR_UNKNOWN;\n\treturn CHIAKI_ERR_SUCCESS;\n}\n"));
    }

    /// <summary>And the callee of an assert is the call in the statement before it.</summary>
    [Fact]
    public void TheCalleeIsReadFromTheStatementBefore()
    {
        const string code =
            "{\n\tChiakiErrorCode err = chiaki_mutex_lock(&m);\n\tassert(err == CHIAKI_ERR_SUCCESS);\n}";

        IReadOnlyList<(string Callee, string Assert)> pairs = AssertedErrorCodes.WithCallees(code);

        Assert.Equal("chiaki_mutex_lock", Assert.Single(pairs).Callee);

        // Written across a line break, which a line-indexed reader would miss.
        const string wrapped =
            "{\n\tChiakiErrorCode err = chiaki_cond_timedwait_pred(\n\t\t&c, &m, 1000, p, u);\n\tassert(err == CHIAKI_ERR_SUCCESS);\n}";

        Assert.Equal("chiaki_cond_timedwait_pred", Assert.Single(AssertedErrorCodes.WithCallees(wrapped)).Callee);
    }
}
