using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP638: the consumers PP295's deletion has to answer for, held against the tree.
///
/// The measurement itself is a build with four lines commented out and cannot live in a test - the
/// same limit PP565 recorded for PP33's. What a test CAN hold is that the tree the linker answered
/// about is the tree in front of it, and that the consumer nobody had written down is really there.
/// </summary>
public class StreamConnectionConsumersTests
{
    /// <summary>
    /// PP638: session.c drives the stream connection, which §PP295 does not say.
    ///
    /// THE ONE THE MEASUREMENT ADDED. Its section names the video receiver's callers - lib and the
    /// shim - and streamconnection.c has a caller of its own that no reading had recorded. It is
    /// PP28's subject, so the deletion cannot land before PP28 does.
    ///
    /// PP758: OR DROVE IT, once PP696 has run. The consumer this recorded is the one that commit
    /// removes, so the claim is about a shape of the tree - and it is asserted in both directions,
    /// because a check that stopped asking after the flip would not notice a call coming back.
    /// </summary>
    [Fact]
    public void SessionDrivesTheStreamConnection()
    {
        if (StreamConnectionConsumers.LocateSession() is not { } path)
            return;

        string source = File.ReadAllText(path);
        IReadOnlyList<string> called = StreamConnectionConsumers.StillCalledBySession(source);

        if (FramePathConsumers.SessionShape() == ConsumerShape.Asking)
        {
            Assert.Equal(StreamConnectionConsumers.SessionCalls, called);
            return;
        }

        Assert.Empty(called);
        Assert.True(
            FramePathConsumers.WasActuallyRead(ConsumerKind.Session, source),
            "session.c drives nothing, and holds none of what survives the flip either");
    }

    /// <summary>
    /// PP638: and one of them carries no prefix, which is PP564's trap a second time.
    ///
    /// `stream_connection_send_idr_request` is exported without `chiaki_`, so a sweep keyed on that
    /// prefix walks straight past it. The first instance was holepunch_session_create_offer and it
    /// cost PP564 a linker run to find; this one would have cost the same.
    ///
    /// Asserted against the header, so the claim is about what the library exports rather than about
    /// what this file remembers.
    /// </summary>
    [Fact]
    public void OneOfThemCarriesNoPrefix()
    {
        Assert.DoesNotContain(
            "chiaki_", StreamConnectionConsumers.UnprefixedExport, StringComparison.Ordinal);

        // The same shape PP564 named, so the two are recognisably one trap and not two oddities.
        Assert.DoesNotContain("chiaki_", HolepunchConsumers.UnprefixedExport, StringComparison.Ordinal);

        if (SanitizerSource.LocateRelative(@"lib\include\chiaki\streamconnection.h") is not { } header)
            return;

        Assert.Contains(
            StreamConnectionConsumers.UnprefixedExport,
            File.ReadAllText(header), StringComparison.Ordinal);
    }

    /// <summary>
    /// PP638: the four files the measurement took out are still the four in the build.
    ///
    /// PP565's half: the recorded result is about a tree, and a tree where these moved is a
    /// different one. A file that left on its own would make the seventeen symbols a number about
    /// something else, still sitting here looking measured.
    /// </summary>
    [Fact]
    public void TheMeasuredTreeIsStillThis()
    {
        if (SanitizerSource.LocateRelative(@"lib\CMakeLists.txt") is not { } path)
            return;

        string cmake = File.ReadAllText(path);
        ConsumerShape shape = StreamConnectionConsumers.ShapeOfTheList(cmake);

        // PP761: OR THE FOUR HAVE GONE TOGETHER, which is that same rule satisfied rather than
        // broken. Partial is the case PP565 was actually about - one file leaving on its own - and
        // it fails on either side, which is why the shape has three answers and not two.
        Assert.NotEqual(ConsumerShape.Partial, shape);

        foreach (string relative in StreamConnectionConsumers.MeasuredAsTheListSpellsThem)
        {
            if (shape == ConsumerShape.Asking)
                Assert.Contains(relative, cmake, StringComparison.Ordinal);
            else
                Assert.DoesNotContain(relative, cmake, StringComparison.Ordinal);
        }

        // And an unreadable list cannot pass for a deletion: what stays has to still be there.
        Assert.True(
            StreamConnectionConsumers.TheListWasActuallyRead(cmake),
            "lib's list names none of the files that stay, so it is not lib's list");
    }

    /// <summary>
    /// PP761: the shape reader itself, on text rather than on whichever tree this runs against.
    ///
    /// One side of the flip exists here at a time, and it is the side that already worked. So both
    /// are asked directly - and the middle answer, the one file that left alone, is the whole reason
    /// PP565 wrote the rule this check enforces.
    /// </summary>
    [Fact]
    public void TheListReaderTellsTheThreeStatesApart()
    {
        const string Stays = "src/takion.c\n\t\tsrc/session.c\n\t\tsrc/ctrl.c";

        string asking = $"{Stays}\n\t\tsrc/streamconnection.c\n\t\tsrc/videoreceiver.c"
            + "\n\t\tsrc/frameprocessor.c\n\t\tsrc/fec.c";
        Assert.Equal(ConsumerShape.Asking, StreamConnectionConsumers.ShapeOfTheList(asking));

        // One gone on its own, which is a tree the seventeen symbols were not measured against.
        string partial = $"{Stays}\n\t\tsrc/videoreceiver.c\n\t\tsrc/frameprocessor.c\n\t\tsrc/fec.c";
        Assert.Equal(ConsumerShape.Partial, StreamConnectionConsumers.ShapeOfTheList(partial));

        Assert.Equal(ConsumerShape.Silent, StreamConnectionConsumers.ShapeOfTheList(Stays));
        Assert.True(StreamConnectionConsumers.TheListWasActuallyRead(Stays));

        // And an empty list is silent about the four and is not lib's.
        Assert.Equal(ConsumerShape.Silent, StreamConnectionConsumers.ShapeOfTheList(""));
        Assert.False(StreamConnectionConsumers.TheListWasActuallyRead(""));
    }

    /// <summary>
    /// PP638: and the C suite's four are really there, because a deletion that forgot them fails at
    /// link time in a commit that thought it was finished.
    /// </summary>
    [Fact]
    public void TheSuiteLinksWhatWouldStopBeingBuilt()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        foreach (string relative in StreamConnectionConsumers.SuiteFiles)
            Assert.True(File.Exists(Path.Combine(root, relative)), $"{relative} is gone");
    }

    /// <summary>
    /// PP638: and the shim is named, which PP584 made the invariant for every deletion line.
    ///
    /// It reaches past the files being deleted into what they link - `create_matrix` is jerasure's,
    /// not libchiaki's - so the seam is a consumer of the deletion's dependencies too.
    /// </summary>
    [Fact]
    public void TheShimIsNamedAndReachesPastTheFiles()
    {
        Assert.Equal(@"shim\chiaki_shim.c", StreamConnectionConsumers.ShimRelativePath);
        Assert.True(StreamConnectionConsumers.ShimSymbols > 0);

        if (StreamConnectionConsumers.LocateShim() is not { } path)
            return;

        Assert.Contains("create_matrix", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
