using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP731: the two crypts a bang starts, and the window one of them remembers.
///
/// PP729 decides a bang and hands the keying to a seam. Behind that seam is
/// stream_connection_init_crypt, and nothing managed did any of it: PP415's derivation, PP416's key
/// stream and PP418's GMAC window are all functions, and a session needs the thing that holds what
/// they work from.
///
/// THE INDEX IS THE WHOLE DIFFERENCE between the two. One secret and one handshake key derive both,
/// and only the byte fed in separates what this client encrypts with from what it decrypts with - a
/// port that used one crypt both ways would produce noise in both directions with nothing failing.
/// </summary>
public class ManagedGkCryptTests(ITestOutputHelper output)
{
    private static readonly byte[] HandshakeKey =
        [0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
         0xa8, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf];

    private static readonly byte[] Secret =
        [.. Enumerable.Range(0, GkDerivation.EcdhSecretSize).Select(one => (byte)(one * 7))];

    private static string? Read()
    {
        string? path = ManagedGkCryptSource.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE TWO ARE DIFFERENT, and only the index made them so.
    ///
    /// Same secret, same handshake key, and every piece of derived material differs. This is the
    /// assertion a port that built one crypt and used it twice would fail.
    /// </summary>
    [Fact]
    public void TheLocalAndRemoteStreamsShareNothingButTheirInputs()
    {
        ManagedGkCryptPair pair = ManagedGkCryptPair.Derive(HandshakeKey, Secret);

        Assert.Equal(2, pair.Local.Index);
        Assert.Equal(3, pair.Remote.Index);

        output.WriteLine($"local key {Convert.ToHexString([.. pair.Local.KeyBase])}");
        output.WriteLine($"remote key {Convert.ToHexString([.. pair.Remote.KeyBase])}");

        Assert.NotEqual(pair.Local.KeyBase, pair.Remote.KeyBase);
        Assert.NotEqual(pair.Local.Iv, pair.Remote.Iv);
        Assert.NotEqual(pair.Local.GmacKeyBase, pair.Remote.GmacKeyBase);

        // And the key streams they produce at the same position differ too.
        Assert.NotEqual(pair.Local.KeyStream(0, 32), pair.Remote.KeyStream(0, 32));
    }

    /// <summary>A crypt starts on window zero, holding the key that window derived.</summary>
    [Fact]
    public void ACryptStartsOnWindowZero()
    {
        ManagedGkCrypt crypt = ManagedGkCrypt.Derive(2, HandshakeKey, Secret);

        Assert.Equal(0ul, crypt.GmacIndexCurrent);
        Assert.Equal(crypt.GmacKeyBase, crypt.GmacKeyCurrent);
        Assert.Equal(
            GkDerivation.GmacKey(0, [.. crypt.KeyBase], [.. crypt.Iv]),
            crypt.GmacKeyBase);
    }

    /// <summary>
    /// THE WINDOW MOVES ONE WAY. A packet ahead refreshes and is kept; one behind is not.
    ///
    /// PP418 wrote the choice down because rolling backwards makes every packet after it fail. This
    /// is the object that would have to do the rolling, so this is where that cannot happen.
    /// </summary>
    [Fact]
    public void APacketAheadRefreshesAndOneBehindDoesNot()
    {
        ManagedGkCrypt crypt = ManagedGkCrypt.Derive(2, HandshakeKey, Secret);

        byte[] first = crypt.GmacKeyFor(1);
        Assert.Equal(0ul, crypt.GmacIndexCurrent);
        Assert.Equal(crypt.GmacKeyBase, first);

        // Two windows on: the session advances and keeps the key.
        byte[] ahead = crypt.GmacKeyFor(GmacKeyWindow.RefreshKeyPos * 2 + 1);

        output.WriteLine($"advanced to window {crypt.GmacIndexCurrent}");

        Assert.Equal(2ul, crypt.GmacIndexCurrent);
        Assert.Equal(ahead, crypt.GmacKeyCurrent);
        Assert.NotEqual(first, ahead);

        // A straggler from window zero gets its own key and leaves the session where it is.
        byte[] behind = crypt.GmacKeyFor(1);

        Assert.Equal(2ul, crypt.GmacIndexCurrent);
        Assert.Equal(ahead, crypt.GmacKeyCurrent);
        Assert.Equal(first, behind);
    }

    /// <summary>
    /// The boundary belongs to the window below it, which the holder inherits from PP418.
    ///
    /// Position 45000 is window zero, not one. Asserted through the crypt rather than through the
    /// window's own function, because this is the object that acts on the answer.
    /// </summary>
    [Fact]
    public void TheWindowBoundaryBelongsBelow()
    {
        ManagedGkCrypt crypt = ManagedGkCrypt.Derive(3, HandshakeKey, Secret);

        crypt.GmacKeyFor(GmacKeyWindow.RefreshKeyPos);
        Assert.Equal(0ul, crypt.GmacIndexCurrent);

        crypt.GmacKeyFor(GmacKeyWindow.RefreshKeyPos + 1);
        Assert.Equal(1ul, crypt.GmacIndexCurrent);
    }

    /// <summary>The build order, and the one rung that has anything to release.</summary>
    [Fact]
    public void OnlyAFailedRemoteHasSomethingToRelease()
    {
        Assert.Equal(
            [GkCryptStep.Local, GkCryptStep.Remote, GkCryptStep.SetOnTakion],
            ManagedGkCryptPair.BuildOrder);

        Assert.Empty(ManagedGkCryptPair.ReleaseAfter(GkCryptStep.Local));
        Assert.Equal([GkCryptStep.Local], ManagedGkCryptPair.ReleaseAfter(GkCryptStep.Remote));
        Assert.Empty(ManagedGkCryptPair.ReleaseAfter(GkCryptStep.SetOnTakion));
    }

    /// <summary>
    /// THE DRIFT CHECKS: the indices, the release and the pairing, still as the C has them.
    /// </summary>
    [Fact]
    public void TheCStillBuildsTwoAtTheseIndicesAndSetsThemTogether()
    {
        if (Read() is not { } source)
            return;

        string? body = ManagedGkCryptSource.InitCryptBody(source);
        Assert.NotNull(body);

        IReadOnlyList<int> indices = ManagedGkCryptSource.IndicesIn(body);

        output.WriteLine($"indices {string.Join(", ", indices)}");

        Assert.Equal(
            [ManagedGkCryptPair.LocalIndex, ManagedGkCryptPair.RemoteIndex],
            indices.Select(one => (byte)one));

        Assert.True(
            ManagedGkCryptSource.AFailedRemoteStillFreesTheLocalProperly(body),
            "a failed remote no longer releases the local one through chiaki_gkcrypt_free");

        Assert.True(
            ManagedGkCryptSource.ThePairIsStillSetTogether(body),
            "the two crypts are no longer handed to the takion together, after both are built");
    }
}
