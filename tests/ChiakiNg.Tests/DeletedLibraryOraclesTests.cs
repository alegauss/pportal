using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP660, under PP33: the shim is an oracle for both libraries PP33 deletes, and only one had been
/// counted.
///
/// PP653 asked the linker what holds holepunch.c in the build. Its answer was true and narrow:
/// json-c was still LINKED at the time, so the shim's own json-c calls resolved and the linker had
/// nothing to say about them. An attempt at PP655's flip found the rest, by failing to link.
/// </summary>
public class DeletedLibraryOraclesTests(ITestOutputHelper output)
{
    private static string? Shim()
        => DeletedLibraryOracles.LocateShim() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// The shim exports a json oracle, and it is the size the flip has to carry.
    ///
    /// Fifteen wrappers today. Counted rather than named, for the reason the holepunch nine turned
    /// out to be ten: a number typed once stops being right somewhere nobody is looking.
    /// </summary>
    [Fact]
    public void TheShimWrapsJsonCAsWellAsHolepunch()
    {
        if (Shim() is not { } shim)
            return;

        IReadOnlyList<string> json = DeletedLibraryOracles.JsonWrappers(shim);
        output.WriteLine($"{json.Count} json wrappers: {string.Join(", ", json)}");

        Assert.NotEmpty(json);
        Assert.All(json, name => Assert.StartsWith(
            DeletedLibraryOracles.JsonWrapperPrefix, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// And the flip's surface is both oracles together, which is more than PP655 was sized from.
    ///
    /// The number is what the attempt bought. PP655 was written from ten undefined references and
    /// the flip has to carry every wrapper that calls either library - so its first step is not
    /// finished, and the assertion says so with a count rather than a paragraph.
    /// </summary>
    [Fact]
    public void TheFlipSurfaceIsLargerThanTheLinkerReported()
    {
        if (Shim() is not { } shim)
            return;

        int surface = DeletedLibraryOracles.FlipSurface(shim);
        output.WriteLine($"flip surface: {surface} exports");

        Assert.True(
            surface > HolepunchShimSurface.UndefinedReferences.Count,
            $"the flip's surface is {surface} and the linker named "
                + $"{HolepunchShimSurface.UndefinedReferences.Count}; if these are equal the json "
                + "oracle has gone and PP655's order can be re-read as written");
    }

    /// <summary>
    /// The reader tells a call from a name, which is what the holepunch count needed too.
    ///
    /// Both directions, because a reader that matched the word would find the include line and the
    /// comment above it and report the whole file as an oracle.
    /// </summary>
    [Theory]
    [InlineData("	return json_tokener_parse(text);", true)]
    [InlineData("	if(!json_object_object_get_ex((json_object *)node, key, &found))", true)]
    [InlineData("#include <json-c/json_object.h>", false)]
    [InlineData("/* PP33: json-c, reachable so the managed replacement can be held against it. */", false)]
    [InlineData("	json_object *found = NULL;", false)]
    public void ANameIsNotACall(string line, bool calls)
        => Assert.Equal(calls, DeletedLibraryOracles.CallsJsonC(line));
}
