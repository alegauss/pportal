using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP656, PP655's first step under PP33: the seam's two shapes, asked once so its models can be
/// converted before the flip rather than during it.
///
/// This is PP630's mechanism one layer along. PP630 asked whether session.c still names the
/// holepunch handle and PP631 converted ten models through the answer; the flip then edited the C
/// and no test file. PP655's flip needs the same preparation, and this is the question it turns on.
///
/// PP662 CHANGED WHERE THE ANSWER COMES FROM. It was the header's text, which is the right file and
/// the wrong reader: the flip gates the declarations with an #ifdef rather than deleting them, so
/// the text says Wrapping on a build that exports none of them. An attempt at the flip turned 128
/// assertions red saying so. The build answers now, through an export it carries either way.
/// </summary>
public class ShimHolepunchShapeTests(ITestOutputHelper output)
{
    /// <summary>
    /// Exactly one shape answers, which is what stops the guard becoming a way of not looking.
    ///
    /// The failure this catches is silent in both directions: two answers is a shape nothing
    /// modelled, and none at all is every check on both sides declining while the header sits there.
    /// PP56 and PP226 are both the same defect one subsystem out.
    /// </summary>
    [Fact]
    public void ExactlyOneShapeAnswersOnThisTree()
        => Assert.True(ShimHolepunchShape.ExactlyOneShapeAnswers());

    /// <summary>
    /// Today it is the wrapping one, and every one of the nine is still declared.
    ///
    /// The assertion that runs on this side. When the flip lands it stops answering and the one
    /// below starts, which is the whole point of the pair - neither side is a check that declined.
    /// </summary>
    [Fact]
    public void TheSeamStillWrapsAndAllNineAreDeclared()
    {
        if (ShimHolepunchShape.WrappingHeader() is not { } header)
            return;

        IReadOnlyList<string> declared = ShimHolepunchShape.StillDeclaredIn(header);
        output.WriteLine($"{declared.Count} of {ShimHolepunchShape.GoneWhenBare.Count} declared");

        Assert.Equal(ShimHolepunchShape.GoneWhenBare, declared);
    }

    /// <summary>
    /// And once it is bare, none of them is. The assertion that says the flip happened.
    ///
    /// It declines today and that is correct: what would be wrong is for it to decline on a tree
    /// where the header IS bare, which is what the pair prevents.
    /// </summary>
    [Fact]
    public void OnceItIsBareNoneOfTheNineIsDeclared()
    {
        if (ShimHolepunchShape.BareHeader() is null)
            return;

        // PP661: the BUILD's shape and not the header's text. The flip puts the declarations inside
        // an #ifdef rather than deleting them, so StillDeclaredIn still finds them - which is what
        // turned 128 assertions red the first time this was asked of the file.
        Assert.Equal(ShimShape.Bare, ShimHolepunchShape.OfTheBuild());
    }

    /// <summary>
    /// The shape is keyed on the header the census reads, which is what makes the two agree.
    ///
    /// NativeSeam holds the host's imports against the shim's HEADERS. Keying this on the same file
    /// is what stops a flip satisfying one and breaking the other - the hazard PP655 named, where
    /// gating the bodies alone leaves the census green and the DLL nine exports short.
    /// </summary>
    [Fact]
    public void TheShapeIsKeyedOnTheFileTheCensusReads()
    {
        Assert.Contains(ShimHolepunchShape.HeaderRelativePath, NativeSeam.HeaderRelativePaths);
        Assert.Equal(ShimHolepunchShape.HeaderRelativePath, HolepunchFileOrder.ShimHeaderRelativePath);
    }

    /// <summary>The reader tells the two shapes apart, both ways.</summary>
    [Theory]
    [InlineData("void chiaki_shim_holepunch_session_init(void);", ShimShape.Wrapping)]
    [InlineData("void chiaki_shim_takion_close(void *takion);", ShimShape.Bare)]
    public void TheKeyDeclarationDecidesTheShape(string header, ShimShape shape)
        => Assert.Equal(shape, ShimHolepunchShape.Of(header));

    /// <summary>
    /// The device id is not one of the nine, and that is PP654 rather than an oversight.
    ///
    /// Its wrapper may stay or go without the seam changing shape, because nothing the host runs
    /// reaches it any more - it is the oracle for a format now. A list that included it would make
    /// the bare shape depend on a wrapper whose presence means nothing.
    /// </summary>
    [Fact]
    public void TheDeviceIdIsNotPartOfTheShape()
    {
        Assert.DoesNotContain(
            "chiaki_shim_generate_client_device_uid", ShimHolepunchShape.GoneWhenBare);
        Assert.Equal(9, ShimHolepunchShape.GoneWhenBare.Count);
    }
}
