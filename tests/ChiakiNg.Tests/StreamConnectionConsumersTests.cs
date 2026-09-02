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
    /// </summary>
    [Fact]
    public void SessionDrivesTheStreamConnection()
    {
        if (StreamConnectionConsumers.LocateSession() is not { } path)
            return;

        Assert.Equal(
            StreamConnectionConsumers.SessionCalls,
            StreamConnectionConsumers.StillCalledBySession(File.ReadAllText(path)));
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

        foreach (string relative in StreamConnectionConsumers.Measured)
        {
            Assert.Contains(
                relative.Replace(@"lib\", "", StringComparison.Ordinal).Replace('\\', '/'),
                cmake, StringComparison.Ordinal);
        }
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
