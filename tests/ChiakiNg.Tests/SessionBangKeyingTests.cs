using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP773: the derivation that was behind IBangKeying, and what it refuses.
///
/// PP729 wrote the bang handler with the keying behind a seam, and SeamReach listed that seam as
/// deliberate. PP773's first commit joined the arrivals to the flags and found what a deliberate
/// seam with nothing behind it costs: a console's bang reached the handler and was refused at the
/// derive, one wait further than before and still not a stream.
///
/// WHAT IS TESTABLE HERE IS THE SEAM AND NOT THE SECRET, which is the same division PP766's readers
/// are held under. The session's ecdh pair exists only across the stream phase and this machine's
/// gate has no console, so what these hold is that the derivation refuses rather than answering with
/// thirty-two zeroes - and that the crypt is not built on a derive that did not happen. A keying
/// that returned a secret for an unconnected session would key a stream against a pair no console
/// ever saw, which does not fail: it produces garbage the decoder reports as a corrupt frame.
///
/// THE SECRET ITSELF IS THE LIVE RUN'S TO SHOW, and it is what PP773's criterion asks for.
/// </summary>
public class SessionBangKeyingTests(ITestOutputHelper output)
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
    /// THE SHIM IS REACHABLE AND IT REFUSES, which are one assertion on an unconnected session.
    ///
    /// Reachable because a P/Invoke to an export that is not there throws rather than answering, so
    /// a null coming back is the boundary crossed and the C saying no. Refusing because the pair is
    /// created on the line before the run and freed on the line after: outside the stream phase
    /// there is nothing to derive against.
    /// </summary>
    [Fact]
    public void ASessionOutsideItsStreamPhaseDerivesNothing()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        byte[]? secret = SessionBigMaterial.DeriveSecret(
            session,
            [.. Enumerable.Range(0, 65).Select(one => (byte)one)],
            [.. Enumerable.Range(0, 32).Select(one => (byte)(0x80 + one))]);

        output.WriteLine(secret is null ? "no secret, as expected" : $"secret of {secret.Length} bytes");

        Assert.Null(secret);
    }

    /// <summary>
    /// AND AN EMPTY KEY OR SIGNATURE NEVER CROSSES, because a bang carrying neither is not one.
    ///
    /// BangHandler refuses those before the derive - NoEcdhPubKey and NoEcdhSig are two of its four
    /// - so this is a second guard on a fact enforced upstream, kept because the reader is public
    /// and its caller's ordering is not this side's to assume.
    /// </summary>
    [Fact]
    public void AnEmptyHalfIsRefusedWithoutCrossing()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        Assert.Null(SessionBigMaterial.DeriveSecret(session, [], [1, 2, 3]));
        Assert.Null(SessionBigMaterial.DeriveSecret(session, [1, 2, 3], []));
    }

    /// <summary>
    /// THE CRYPT IS NOT BUILT ON A DERIVE THAT DID NOT HAPPEN.
    ///
    /// The C reaches stream_connection_init_crypt only past a successful derivation, so this order
    /// is one the handler never asks for - and answering false is what keeps a pair from being
    /// derived out of a null secret if it ever did.
    /// </summary>
    [Fact]
    public void TheCryptIsRefusedBeforeADerive()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        var takion = new ManagedTakion(0x0000_7731);
        var keying = new SessionBangKeying(session, takion);

        Assert.False(keying.InitCrypt());
        Assert.Null(keying.Crypt);

        // And the takion is left as it was: nothing sends under a crypt that was not built.
        Assert.Null(takion.LocalCrypt);
        Assert.False(takion.CryptAvailable);
    }

    /// <summary>
    /// A BANG THROUGH THE WHOLE PATH IS REFUSED WHERE THE SESSION IS NOT IN ITS STREAM PHASE.
    ///
    /// The join, end to end: the arrivals route by state, the handler reads the message, the keying
    /// crosses to the session and the flag the outcome leaves is the one the run reads. What is
    /// asserted is that a refusal reaches state_failed rather than throwing - a keying whose derive
    /// raised would take the takion's receive thread down with it.
    /// </summary>
    [Fact]
    public void ABangOnAnUnconnectedSessionFailsTheStateRatherThanThrowing()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        var takion = new ManagedTakion(0x0000_7732);
        var sent = new StreamArrivalsTests.Sent();
        ManagedStreamRunHost host = StreamArrivalsTests.HostOn(takion, sent);

        var arrivals = new StreamArrivals(host, sent, new SessionBangKeying(session, takion));

        host.BeginState(StreamState.ExpectBang);

        ArrivalReading reading = arrivals.Data(TakionDataType.Protobuf, StreamArrivalsTests.BangBytes());

        output.WriteLine($"{reading}");

        Assert.Equal(BangOutcome.Refused, reading.Bang);
        Assert.True(host.Flags.Failed);
        Assert.False(host.Flags.Finished);
    }

    /// <summary>
    /// The secret's size is the C's own, read out of ecdh.h rather than remembered.
    ///
    /// The derivation takes no size and writes exactly this many bytes, so a buffer this side got
    /// wrong is a stack overrun in the shim rather than a truncation - which is why the shim refuses
    /// a short one rather than filling what it can.
    /// </summary>
    [Fact]
    public void TheSecretIsTheSizeTheHeaderSays()
    {
        Assert.Equal(32, SessionBigMaterial.EcdhSecretBytes);

        if (SanitizerSource.LocateRelative(@"lib\include\chiaki\ecdh.h") is not { } path)
            return;

        Assert.Contains(
            $"#define CHIAKI_ECDH_SECRET_SIZE {SessionBigMaterial.EcdhSecretBytes}",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE SHIM DERIVES AGAINST THE SESSION'S OWN PAIR, which is the whole of why it takes one.
    ///
    /// The public half that went out in the BIG came from session-&gt;ecdh, so the private half that
    /// signed it is the only one the console's answer can be derived against. A shim that created a
    /// fresh pair would answer thirty-two plausible bytes for every bang, and the session would key,
    /// stream and decode nothing - which is the failure PP26 warns is the expensive kind.
    /// </summary>
    [Fact]
    public void TheShimUsesTheSessionsPairAndItsHandshakeKey()
    {
        if (SanitizerSource.LocateRelative(@"shim\chiaki_shim.c") is not { } path)
            return;

        string body = CFunction.Body(
            File.ReadAllText(path), "CHIAKI_SHIM_API bool chiaki_shim_session_derive_secret(")
            ?? throw new InvalidOperationException("the shim no longer has a derivation");

        Assert.Contains("&self->ecdh", body, StringComparison.Ordinal);
        Assert.Contains("self->handshake_key", body, StringComparison.Ordinal);

        // And nothing creates a pair here: the one this derives against is the session's.
        Assert.DoesNotContain("chiaki_ecdh_init", body, StringComparison.Ordinal);
    }
}
