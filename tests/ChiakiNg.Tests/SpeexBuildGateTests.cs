using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP32: speexdsp is the Qt client's, and the build says so now.
///
/// The three claims §PP32 made in prose, held against the tree instead: lib/ references speex
/// nowhere, gui/ is the only thing that links it, and the probe runs only where gui/ is built.
/// </summary>
public class SpeexBuildGateTests(ITestOutputHelper output)
{
    private static string? RootCMake()
        => SpeexBuildGate.Locate(SpeexBuildGate.RootCMakeRelativePath) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>
    /// The probe follows the client, so a default configure does not go looking for it.
    ///
    /// This is the change PP32 made. PP632 turned the client's build off and the probe went on
    /// running, announcing a feature nothing linked - and the announcement is the part that matters,
    /// because it is what a reader checks before asking whether a dependency is still needed.
    /// </summary>
    [Fact]
    public void TheProbeRunsOnlyWhereTheClientIsBuilt()
    {
        if (RootCMake() is not { } cmake)
            return;

        Assert.True(
            SpeexBuildGate.TheProbeIsGatedOnTheClient(cmake),
            $"the speexdsp probe is no longer inside a {SpeexBuildGate.ClientOption} guard, so a "
                + "default configure looks for a dependency nothing it builds links");
    }

    /// <summary>
    /// And gui/ is still the only place it is linked, which is what makes the gate correct.
    ///
    /// Without this the gate would be a guess: a second target linking speex would make the guard
    /// above hide a dependency the default build genuinely needs, and the failure would arrive at
    /// link time on somebody else's machine.
    /// </summary>
    [Fact]
    public void OnlyTheClientLinksIt()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<string> linking = SpeexBuildGate.FilesLinkingSpeex(root);
        output.WriteLine(string.Join(", ", linking));

        Assert.Equal([SpeexBuildGate.ClientCMakeRelativePath], linking);
    }

    /// <summary>
    /// And the library references speex nowhere, which is §PP32's correction made checkable.
    ///
    /// The first draft of that section placed the speex stages beside Opus in the library and on the
    /// playback path. Both were wrong: they are the microphone's and they are the client's. That
    /// correction lived in a rationale file, and a ship deletes those - so it lives here.
    /// </summary>
    [Fact]
    public void TheLibraryReferencesSpeexNowhere()
    {
        IReadOnlyList<string> mentioning = SpeexBuildGate.LibFilesMentioningSpeex();

        Assert.True(
            mentioning.Count == 0,
            "lib/ now mentions speex, so the audio path is no longer split the way PP32 measured: "
                + string.Join(", ", mentioning));
    }

    /// <summary>
    /// The gate reader wants the guard on the line that OPENS the block, not anywhere inside it.
    ///
    /// A guard added after the find_package has already run reads like a fix and is not one. Both
    /// shapes are given, because the reader would otherwise be satisfied by the second.
    /// </summary>
    [Theory]
    [InlineData("if(CHIAKI_ENABLE_GUI AND CHIAKI_ENABLE_SPEEX)\n find_package(SpeexDSP QUIET)\nendif()", true)]
    [InlineData("if(CHIAKI_ENABLE_SPEEX)\n find_package(SpeexDSP QUIET)\n if(CHIAKI_ENABLE_GUI)\n endif()\nendif()", false)]
    [InlineData("if(CHIAKI_ENABLE_SPEEX)\n find_package(SpeexDSP QUIET)\nendif()", false)]
    public void TheGuardHasToOpenTheBlock(string cmake, bool gated)
        => Assert.Equal(gated, SpeexBuildGate.TheProbeIsGatedOnTheClient(cmake));

    /// <summary>And no probe at all is the same answer, more strongly.</summary>
    [Fact]
    public void ABuildThatNeverLooksForItPasses()
        => Assert.True(SpeexBuildGate.TheProbeIsGatedOnTheClient("project(chiaki)"));

    /// <summary>
    /// A comment naming the target is not a place the target is linked.
    ///
    /// Both readers learned this from the root CMakeLists on their first run: the paragraph
    /// explaining that speex is linked in exactly one place names it, so scanning raw text reported
    /// the file that documents the rule as a file that breaks it.
    /// </summary>
    [Fact]
    public void AMentionInACommentIsNotALink()
    {
        const string cmake = """
            # PkgConfig::SpeexDSP is linked in exactly one place, and this is not it.
            target_link_libraries(chiaki SDL2::SDL2) # not PkgConfig::SpeexDSP either
            """;

        Assert.DoesNotContain(
            SpeexBuildGate.LinkedTarget, SpeexBuildGate.Code(cmake), StringComparison.Ordinal);
    }

    /// <summary>And code on a line that also carries a comment survives the cut.</summary>
    [Fact]
    public void TheCodeBeforeATrailingCommentIsKept()
    {
        Assert.Contains(
            "target_link_libraries(chiaki PkgConfig::SpeexDSP)",
            SpeexBuildGate.Code("target_link_libraries(chiaki PkgConfig::SpeexDSP) # the one place"),
            StringComparison.Ordinal);
    }
}
