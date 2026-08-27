using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP454, PP33, PP197: the probe packet's geometry is written down once.
///
/// PP236 read the eighty-eight byte packet's offsets out of holepunch.c and PP243 derived from it,
/// saying why. Then PP197 and PP33 each read the same C again, and the tree carried three independent
/// copies of thirteen numbers - all agreeing, which is exactly why no test noticed. Each was checked
/// against the C separately and each was right.
///
/// So the values are asserted equal here, and more usefully the DERIVATION is: a constant that goes
/// back to being a literal fails, and a fourth file declaring its own copy fails.
/// </summary>
public class ProbeGeometryTests
{
    /// <summary>
    /// THE VALUES AGREE, which is what makes the consolidation safe rather than a change.
    ///
    /// This is the test that would have caught the duplication if the three had ever drifted, and it
    /// is worth keeping for the same reason it was worth writing: it names the mapping between three
    /// vocabularies for one packet.
    /// </summary>
    [Fact]
    public void AllThreeVocabulariesDescribeOnePacket()
    {
        Assert.Equal(PunchResponse.Length, NatProbe.Length);
        Assert.Equal(PunchResponse.Length, CandidateRace.MessageLength);
        Assert.Equal(PunchResponse.Length, PunchProbe.Length);

        Assert.Equal(PunchResponse.LocalIdAt, NatProbe.LocalHashedIdOffset);
        Assert.Equal(PunchResponse.ConsoleIdAt, NatProbe.ConsoleHashedIdOffset);
        Assert.Equal(PunchResponse.IdLength, NatProbe.HashedIdLength);
        Assert.Equal(PunchResponse.IdSlot, NatProbe.HashedIdSlot);
        Assert.Equal(PunchResponse.SessionIdsAt, NatProbe.LocalSidOffset);
        Assert.Equal(PunchResponse.SessionIdsAt + 2, NatProbe.ConsoleSidOffset);

        Assert.Equal(PunchResponse.EchoAt, NatProbe.RequestIdOffset);
        Assert.Equal(PunchResponse.EchoAt, CandidateRace.RequestIdOffset);
        Assert.Equal(PunchResponse.EchoAt, PunchProbe.RequestIdAt);

        Assert.Equal(PunchResponse.EchoLength, NatProbe.RequestIdLength);
        Assert.Equal(PunchResponse.EchoLength, CandidateRace.RequestIdLength);
        Assert.Equal(PunchResponse.EchoLength, PunchProbe.RequestIdLength);

        Assert.Equal(PunchResponse.AddressKeyAt, NatProbe.MaskedAddressOffset);
        Assert.Equal(PunchResponse.PortKeyAt, NatProbe.MaskedPortOffset);
        Assert.Equal(PunchResponse.AddressKeyed, NatProbe.MaskedAddressLength);

        Assert.Equal(PunchResponse.RequestType, CandidateRace.RequestType);
        Assert.Equal(PunchResponse.RequestType, PunchProbe.RequestType);
        Assert.Equal(PunchResponse.ResponseType, CandidateRace.ResponseType);
    }

    /// <summary>
    /// And the values agree BECAUSE they are derived, not because three readings happened to match.
    ///
    /// The test above passes either way, which is the whole lesson: agreement is not the property
    /// worth asserting, single-sourcing is.
    /// </summary>
    [Fact]
    public void EveryOneOfThemIsDerivedAndNotWrittenDownAgain()
    {
        if (ProbeGeometry.LocateDirectory() is not { } directory)
            return;

        foreach (DerivedConstant constant in ProbeGeometry.Derived)
        {
            string path = Path.Combine(directory, constant.File);
            Assert.True(File.Exists(path), path);

            Assert.True(
                ProbeGeometry.IsDerived(File.ReadAllText(path), constant.Name),
                $"{constant.File}: {constant.Name} names a number instead of deriving it from "
                    + $"{ProbeGeometry.AuthorityFile}");
        }
    }

    /// <summary>
    /// No fourth copy. A `public const` holding either message type, outside the authority, is
    /// another class reading the same C for itself.
    /// </summary>
    [Fact]
    public void NoOtherFileDeclaresTheMessageTypes()
    {
        Assert.Empty(ProbeGeometry.FilesWithTheirOwnCopy());
    }

    /// <summary>
    /// The reader tells a derivation from a literal, and is not fooled by a doc comment that names
    /// the authority above one.
    /// </summary>
    [Fact]
    public void TheReaderLooksAtTheInitialiserAndNotTheComment()
    {
        const string derived = "    public const int EchoAt = PunchResponse.EchoAt;";
        const string literal = "    public const int EchoAt = 0x4b;";
        const string commented = """
                /// <summary>See <see cref="PunchResponse"/>.</summary>
                public const int EchoAt = 0x4b;
            """;

        Assert.True(ProbeGeometry.IsDerived(derived, "EchoAt"));
        Assert.False(ProbeGeometry.IsDerived(literal, "EchoAt"));
        Assert.False(ProbeGeometry.IsDerived(commented, "EchoAt"));
    }

    /// <summary>
    /// And it answers about the constant being DECLARED, not one mentioned in somebody else's
    /// initialiser - which is the mistake that would make every check above vacuously green.
    /// </summary>
    [Fact]
    public void TheReaderDoesNotAnswerForANameItOnlyReads()
    {
        const string source = "    public const int RequestIdOffset = PunchResponse.EchoAt;";

        Assert.True(ProbeGeometry.IsDerived(source, "RequestIdOffset"));
        Assert.False(ProbeGeometry.IsDerived(source, "EchoAt"));
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.False(ProbeGeometry.IsDerived("", "Length"));
        Assert.False(ProbeGeometry.DeclaresAMessageTypeLiteral(""));

        // And says yes about the thing it is looking for, so the check above is not vacuous.
        Assert.True(
            ProbeGeometry.DeclaresAMessageTypeLiteral("    public const uint RequestType = 0x06000000;"));
    }
}
