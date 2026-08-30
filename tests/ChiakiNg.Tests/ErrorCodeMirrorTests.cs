using ChiakiNg.Native;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP577: the managed error enum against the C one it mirrors.
/// </summary>
public class ErrorCodeMirrorTests
{
    private static string? Header()
        => ErrorCodeMirror.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE TWO AGREE POSITION BY POSITION, which is the only thing that makes the cast sound.
    ///
    /// ChiakiError is cast straight from integers the C returns, and both enums are implicitly
    /// valued - only the first says `= 0`. So a member inserted into the C's enum shifts every value
    /// after it and every cast quietly starts meaning the neighbour, with nothing failing.
    /// </summary>
    [Fact]
    public void TheManagedEnumMirrorsTheCOneInOrder()
    {
        if (Header() is not { } header)
            return;

        IReadOnlyList<string> said = ErrorCodeMirror.Disagreements(header);
        Assert.True(said.Count == 0, string.Join("; ", said));
    }

    /// <summary>Twenty-two each, read from the header rather than written down here.</summary>
    [Fact]
    public void TheCountsMatch()
    {
        if (Header() is not { } header)
            return;

        Assert.Equal(ErrorCodeMirror.MembersIn(header).Count, ErrorCodeMirror.Managed.Count);
        Assert.NotEmpty(ErrorCodeMirror.Managed);
    }

    /// <summary>
    /// An INSERTION is caught, which is the case that matters and the one a set comparison misses:
    /// every name is still present on both sides, and everything after the insertion has moved.
    /// </summary>
    [Fact]
    public void AMemberInsertedInTheMiddleIsCaught()
    {
        const string doctored = """
            typedef enum
            {
                CHIAKI_ERR_SUCCESS = 0,
                CHIAKI_ERR_WEDGED_IN_HERE,
                CHIAKI_ERR_UNKNOWN,
            } ChiakiErrorCode;
            """;

        IReadOnlyList<string> said = ErrorCodeMirror.Disagreements(doctored);

        Assert.NotEmpty(said);
        Assert.Contains(said, one => one.Contains("at 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// The comparison is on letters, not casing: CHIAKI_ERR_HTTP_NONOK is HttpNonOk here, and no
    /// mechanical split of the C's name produces that spelling.
    /// </summary>
    [Fact]
    public void TheJoinIsLettersRatherThanCasing()
    {
        Assert.Equal(
            ErrorCodeMirror.Normalise("CHIAKI_ERR_HTTP_NONOK"),
            ErrorCodeMirror.Normalise("HttpNonOk"));

        Assert.Equal(
            ErrorCodeMirror.Normalise("CHIAKI_ERR_BUF_TOO_SMALL"),
            ErrorCodeMirror.Normalise("BufTooSmall"));

        Assert.NotEqual(
            ErrorCodeMirror.Normalise("CHIAKI_ERR_TIMEOUT"),
            ErrorCodeMirror.Normalise("InvalidResponse"));
    }
}
