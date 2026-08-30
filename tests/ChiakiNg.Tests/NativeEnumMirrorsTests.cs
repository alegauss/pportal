using ChiakiNg.Native;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP577, PP578: every managed enum a C value is cast into, against the C enum it mirrors.
/// </summary>
public class NativeEnumMirrorsTests
{
    /// <summary>
    /// ALL THREE AGREE POSITION BY POSITION, which is the only thing making the casts sound.
    ///
    /// ChiakiError, ChiakiEventType and ChiakiQuitReason are each cast straight from integers the C
    /// returns, and all three C enums are implicitly valued - two do not even say `= 0` on the first
    /// member. So a member inserted upstream shifts every value after it and each cast quietly
    /// starts meaning the neighbour, with nothing failing.
    ///
    /// PP577 held the first and stopped; the other two are cast on the next two lines of the same
    /// file.
    /// </summary>
    [Fact]
    public void EveryMirrorMatchesTheCEnumInOrder()
    {
        foreach (NativeEnumMirror mirror in NativeEnumMirrors.All)
        {
            if (NativeEnumMirrors.Locate(mirror) is not { } path)
                continue;

            IReadOnlyList<string> said =
                NativeEnumMirrors.Disagreements(mirror, File.ReadAllText(path));

            Assert.True(said.Count == 0, string.Join("; ", said));
        }
    }

    /// <summary>
    /// Three of them, and every one a type a C value is cast into. A fourth such cast added without
    /// a row here is the gap PP577 left and PP578 closed.
    /// </summary>
    [Fact]
    public void TheMirrorsAreTheThreeCastFromNative()
    {
        Assert.Equal(3, NativeEnumMirrors.All.Count);

        Assert.Equal(
            ["ChiakiError", "ChiakiEventType", "ChiakiQuitReason"],
            NativeEnumMirrors.All.Select(one => one.Managed.Name));

        // Each reads a real header, and each carries members.
        foreach (NativeEnumMirror mirror in NativeEnumMirrors.All)
        {
            if (NativeEnumMirrors.Locate(mirror) is not { } path)
                continue;

            Assert.NotEmpty(NativeEnumMirrors.MembersIn(File.ReadAllText(path), mirror.Prefix));
        }
    }

    /// <summary>
    /// An INSERTION is caught, which is the case that matters and the one a set comparison misses:
    /// every name is still present on both sides, and everything after it has moved.
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

        IReadOnlyList<string> said =
            NativeEnumMirrors.Disagreements(NativeEnumMirrors.All[0], doctored);

        Assert.NotEmpty(said);
        Assert.Contains(said, one => one.Contains("at 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// The join is letters, not casing: CHIAKI_ERR_HTTP_NONOK is HttpNonOk, and no mechanical split
    /// of the C's name produces that spelling.
    /// </summary>
    [Fact]
    public void TheJoinIsLettersRatherThanCasing()
    {
        Assert.Equal(
            NativeEnumMirrors.Normalise("CHIAKI_ERR_HTTP_NONOK", "CHIAKI_ERR_"),
            NativeEnumMirrors.Normalise("HttpNonOk", "CHIAKI_ERR_"));

        Assert.NotEqual(
            NativeEnumMirrors.Normalise("CHIAKI_ERR_TIMEOUT", "CHIAKI_ERR_"),
            NativeEnumMirrors.Normalise("InvalidResponse", "CHIAKI_ERR_"));
    }
}
