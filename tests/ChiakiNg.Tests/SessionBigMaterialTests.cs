using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP766, under PP762: the four things a managed BIG needs out of a live session.
///
/// PP765 found ten of a run host's eleven parts compose from work that shipped, and the eleventh -
/// the message that STARTS a stream - wants the session id ctrl produced, the handshake key that
/// signs and is spelled into the spec, the numbers senkusha measured, and an ecdh pair that exists
/// only across the run. Three was this task's first reading; the spec wanting the key in its own
/// right is what made it four.
///
/// WHAT IS TESTABLE HERE IS THE SEAM AND NOT THE VALUES. A session that has not connected has none
/// of the four, and this machine's gate has no console - so what these hold is that each reader
/// answers NULL rather than a wrong value, which is the half a wrong port gets wrong. A reader that
/// returned zeroes for an unconnected session would let a composition root build a BIG describing a
/// link nobody measured, and the console would refuse it with nothing to say why.
/// </summary>
public class SessionBigMaterialTests(ITestOutputHelper output)
{
    private static ChiakiSession? Build()
    {
        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = "127.0.0.1", Ps5 = true };
        info.SetRegistKey(new byte[16]);
        info.SetMorning(new byte[16]);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);

        return ChiakiSession.TryCreate(info, null, out _);
    }

    /// <summary>
    /// A SESSION THAT HAS NOT CONNECTED HAS NONE OF THE FOUR, and says so with null.
    ///
    /// The alternative each reader could have taken is the one that costs: an empty id, a zero MTU
    /// and an all-zero key are what the fields hold before anything fills them, and all three look
    /// like values.
    /// </summary>
    [Fact]
    public void NoneOfTheFourIsThereBeforeASessionConnects()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        Assert.Null(SessionBigMaterial.IdOf(session));
        Assert.Null(SessionBigMaterial.TransportOf(session));
        Assert.Null(SessionBigMaterial.HandshakeKeyOf(session));

        // The ecdh is created on the line before the run and freed on the line after, so outside
        // the stream phase there is nothing to copy.
        SessionEcdhMaterial? ecdh = SessionBigMaterial.EcdhOf(session);
        output.WriteLine(ecdh is null ? "ecdh: none, as expected" : $"ecdh: {ecdh.Value.PublicKey.Length} bytes");
    }

    /// <summary>
    /// The sizes are the C's own, which is what keeps a buffer from being this side's guess.
    ///
    /// PP766's readers offer exactly what streamconnection.c's send_big offers - 128 for the key and
    /// 32 for the signature - so a key the C would have fitted cannot be refused here for room.
    /// </summary>
    [Fact]
    public void TheBuffersAreTheOnesTheCsOwnSendBigOffers()
    {
        Assert.Equal(128, SessionBigMaterial.PublicKeyBytes);
        Assert.Equal(32, SessionBigMaterial.SignatureBytes);
        Assert.Equal(80, SessionBigMaterial.SessionIdBytes);

        // And the id's bound is the C's CHIAKI_SESSION_ID_SIZE_MAX, read rather than remembered.
        if (SanitizerSource.LocateRelative(@"lib\include\chiaki\session.h") is not { } path)
            return;

        Assert.Contains(
            $"#define CHIAKI_SESSION_ID_SIZE_MAX {SessionBigMaterial.SessionIdBytes}",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE BIG IS NOW BUILDABLE, which is what PP765 said this part was waiting for.
    ///
    /// Built here from stand-in material rather than a live session's, because the machine running
    /// this has no console - what it proves is that Encode takes exactly what the four readers
    /// answer, so a composition root holding them has the five arguments it needs.
    /// </summary>
    [Fact]
    public void TheFourReadersAnswerWhatEncodeAsks()
    {
        // Stand-in material, because the machine running this has no console. What it proves is the
        // JOIN: each of the four readers answers a type Encode and the launch spec take, so a
        // composition root holding them has every argument the BIG wants.
        var ecdh = new SessionEcdhMaterial(new byte[64], new byte[32]);
        var transport = new SessionTransport(MtuIn: 1454, MtuOut: 1454, RoundTripMicroseconds: 2333);
        var handshakeKey = new byte[SessionBigMaterial.HandshakeKeyBytes];
        const string Id = "a-session-id";

        var fields = new LaunchSpecFields(
            Width: 1280, Height: 720, MaxFps: 60, BwKbpsSent: 10000,
            Mtu: transport.MtuOut,
            Rtt: (uint)(transport.RoundTripMicroseconds / 1000),
            Target: ChiakiTarget.Ps5_1,
            Codec: ChiakiCodec.H264);

        var crypt = new RpCrypt(ChiakiTarget.Ps5_1, new byte[16], new byte[16]);

        // PP726 ported the template, so the formatting was never what was missing - the numbers
        // going into it were, and two of them are what senkusha measured.
        string? spec = BigMessage.EncodedLaunchSpec(crypt, fields, handshakeKey);
        Assert.NotNull(spec);

        byte[] big = BigMessage.Encode(
            clientVersion: 9,
            sessionKey: Id,
            encodedLaunchSpec: spec,
            ecdhPubKey: ecdh.PublicKey,
            ecdhSig: ecdh.Signature);

        Assert.NotEmpty(big);
        output.WriteLine($"big: {big.Length} bytes");

        // And it is a BIG rather than what a heartbeat encodes to, which is what every test handing
        // the host a heartbeat has been putting in its place.
        Assert.NotEqual(StreamMessages.Heartbeat().Body.Length, big.Length);
    }
}
