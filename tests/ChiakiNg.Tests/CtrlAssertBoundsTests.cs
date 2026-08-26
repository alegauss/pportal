using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP357: no copy in ctrl.c stands on an assert, because the shipped build compiles them out.
/// </summary>
public class CtrlAssertBoundsTests
{
    /// <summary>
    /// The premise, read out of the configured build rather than assumed.
    ///
    /// If a tree were configured Debug the asserts would be real and this whole check would be
    /// about nothing - so the flags are what makes the argument, and they are checked.
    /// </summary>
    [Fact]
    public void TheConfiguredBuildCompilesAssertsOut()
    {
        string? cache = CtrlAssertBounds.LocateCache();
        if (cache is null)
            return;

        Assert.True(
            CtrlAssertBounds.AssertsAreCompiledOut(File.ReadAllText(cache)),
            "this tree is not configured Release with NDEBUG, so PP357's premise needs rechecking");
    }

    /// <summary>
    /// THE CHECK: no assert about a size is the only thing in front of a copy.
    ///
    /// Two keyboard handlers were written that way. A message announcing more text than it carried
    /// was malloc'd at the announced length and memcpy'd out of a 512-byte buffer, into a string
    /// handed to a screen as the text the user was editing.
    /// </summary>
    [Fact]
    public void NoCopyStandsOnAnAssert()
    {
        string? path = CtrlAssertBounds.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> standing =
            CtrlAssertBounds.AssertsStandingInForABound(File.ReadAllText(path));

        Assert.True(
            standing.Count == 0,
            "these asserts are the only bound on a copy, and are not in the shipped binary:\n  "
                + string.Join("\n  ", standing));
    }

    /// <summary>And the reader finds one where there is one, so the check above means something.</summary>
    [Fact]
    public void TheReaderFindsAnAssertStandingInForABound()
    {
        const string asItWas = """
            	if(payload_size < sizeof(CtrlKeyboardOpenMessage))
            		return;

            	msg->text_length = ntohl(msg->text_length);
            	assert(payload_size == sizeof(CtrlKeyboardOpenMessage) + msg->text_length);

            	uint8_t *buffer = malloc((size_t)msg->text_length + 1);
            	memcpy(buffer, payload + sizeof(CtrlKeyboardOpenMessage), msg->text_length);
            """;

        string found = Assert.Single(CtrlAssertBounds.AssertsStandingInForABound(asItWas));

        Assert.Contains("assert(payload_size ==", found, StringComparison.Ordinal);
    }

    /// <summary>And ignores one where a real check stands between the assert and the copy.</summary>
    [Fact]
    public void TheReaderIgnoresAnAssertBehindARealCheck()
    {
        const string fixedUp = """
            	assert(payload_size >= sizeof(Header));

            	if(payload_size != sizeof(Header) + msg->text_length)
            		return;

            	memcpy(buffer, payload + sizeof(Header), msg->text_length);
            """;

        Assert.Empty(CtrlAssertBounds.AssertsStandingInForABound(fixedUp));
    }
}
