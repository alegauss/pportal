using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP653, under PP33: the linker's answer, and the count in the prose that had drifted off it.
///
/// The fourth criterion says libchiaki builds with neither curl nor json-c, and names how to find
/// out: take holepunch.c out of lib's sources and read what fails. Done on 2026-09-03 - everything
/// compiles, one target fails, and the ten references it fails on are all holepunch's.
/// </summary>
public class HolepunchShimSurfaceTests(ITestOutputHelper output)
{
    private static string? Shim()
        => HolepunchShimSurface.LocateShim() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// Every symbol the linker named is one the shim actually calls.
    ///
    /// The recorded list is a build's output and a build's output is not in the tree, so this is
    /// what keeps it honest: if a wrapper goes, the symbol it called stops appearing here and the
    /// record is stale rather than quietly wrong.
    /// </summary>
    [Fact]
    public void TheShimStillCallsEverySymbolTheLinkerNamed()
    {
        if (Shim() is not { } shim)
            return;

        foreach (string symbol in HolepunchShimSurface.UndefinedReferences)
        {
            Assert.Contains(
                symbol + "(", shim, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And there are ten wrappers, which is the number the tree's prose had wrong.
    ///
    /// Three places said nine. It was nine when PP481 wrote them and PP556 added set_recorded as the
    /// tenth, when the prepare became an instance call that records the socket the ctrl punch
    /// produced. Nothing re-derived the count, so a number outlived the commit after the one that
    /// made it true.
    ///
    /// Counted from the shim, both ways: the wrappers that call a holepunch symbol, and the symbols
    /// themselves. They agree at ten because each wrapper calls exactly one.
    /// </summary>
    [Fact]
    public void ThereAreTenWrappersAndTenSymbols()
    {
        if (Shim() is not { } shim)
            return;

        IReadOnlyList<string> wrappers = HolepunchShimSurface.Wrappers(shim);
        output.WriteLine(string.Join(", ", wrappers));

        Assert.Equal(HolepunchShimSurface.UndefinedReferences.Count, wrappers.Count);
        Assert.Equal(10, wrappers.Count);
    }

    /// <summary>
    /// And not one of them is curl's or json-c's, which is the criterion itself.
    ///
    /// PP33's fourth criterion is a claim about what the failure set does NOT contain. The list is
    /// short enough to read, and reading it is the point: a curl symbol appearing here would mean
    /// the deletion is bigger than holepunch.c and the criterion's own method was answering about
    /// the wrong file.
    /// </summary>
    [Fact]
    public void NoUndefinedReferenceBelongsToALibraryPP33Deletes()
    {
        IReadOnlyList<string> wrong =
        [
            .. HolepunchShimSurface.UndefinedReferences
                .Where(HolepunchShimSurface.IsFromADeletedLibrary),
        ];

        Assert.True(
            wrong.Count == 0,
            "the linker failed on a curl or json-c symbol, so taking holepunch.c out does not take "
                + "the libraries with it: " + string.Join(", ", wrong));
    }

    /// <summary>
    /// The reader can tell a curl symbol from a holepunch one, so the check above can fail.
    ///
    /// An absence assertion whose reader matches nothing passes on everything, which is the shape
    /// PP271 named and the one this file would otherwise have.
    /// </summary>
    [Theory]
    [InlineData("curl_easy_setopt", true)]
    [InlineData("json_object_new_object", true)]
    [InlineData("json_tokener_parse", true)]
    [InlineData("chiaki_holepunch_session_init", false)]
    [InlineData("holepunch_session_create_offer", false)]
    public void ACurlSymbolIsRecognisedAndAHolepunchOneIsNot(string symbol, bool deleted)
        => Assert.Equal(deleted, HolepunchShimSurface.IsFromADeletedLibrary(symbol));

    /// <summary>And holepunch.c is still the line the probe comments out.</summary>
    [Fact]
    public void TheSourceEntryIsStillWhereTheProbeExpectsIt()
    {
        if (SanitizerSource.LocateRelative(HolepunchShimSurface.LibCMakeRelativePath) is not { } path)
            return;

        Assert.Contains(
            HolepunchShimSurface.SourceEntry, File.ReadAllText(path), StringComparison.Ordinal);
    }
}
