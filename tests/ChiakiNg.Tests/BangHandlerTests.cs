using ChiakiNg.Protocol;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP729: stream_connection_takion_data_expect_bang - the handler that keys the session.
///
/// PP721 wired PP366's second dispatch layer to the events; the third routes a protobuf by state to
/// one of three handlers, and this was the one with no port at all. It is the last decision between
/// a managed run and a keyed session, so it is PP707's first criterion getting closer rather than
/// its second, which the census already answered.
///
/// TWO KINDS OF NOT-A-BANG, AND ONE OF THEM DOES NOTHING TO THE STATE. A message that will not
/// decode, a disconnect, an early streaminfo and an unrecognised type all leave both flags alone -
/// so the wait runs on. A bang that IS one and is refused sets state_failed, which PP365 established
/// is read by nobody. Both of those are asserted here, because a port that "improved" either would
/// end a wait the C lets run.
/// </summary>
public class BangHandlerTests(ITestOutputHelper output)
{
    /// <summary>A keying that answers what a test tells it to, and records that it was asked.</summary>
    private sealed class Keying(bool derives = true, bool keys = true) : IBangKeying
    {
        public int Derived { get; private set; }

        public int Keyed { get; private set; }

        public byte[] SawPubKey { get; private set; } = [];

        public byte[] SawSig { get; private set; } = [];

        public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig)
        {
            Derived++;
            SawPubKey = remotePubKey.ToArray();
            SawSig = remoteSig.ToArray();

            return derives;
        }

        public bool InitCrypt()
        {
            Keyed++;
            return keys;
        }
    }

    private static byte[] PubKey(int length = 65)
        => [.. Enumerable.Range(0, length).Select(one => (byte)one)];

    private static byte[] Sig(int length = 32)
        => [.. Enumerable.Range(0, length).Select(one => (byte)(0x80 + one))];

    private static byte[] Bang(
        bool versionAccepted = true,
        bool encryptedKeyAccepted = true,
        byte[]? pubKey = null,
        byte[]? sig = null)
        => new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
            BangPayload = new Tkproto.BangPayload
            {
                ServerVersion = 12,
                Token = 7,
                VersionAccepted = versionAccepted,
                EncryptedKeyAccepted = encryptedKeyAccepted,
                SessionKey = "sessionId4321",
                EcdhPubKey = ByteString.CopyFrom(pubKey ?? PubKey()),
                EcdhSig = ByteString.CopyFrom(sig ?? Sig()),
            },
        }.ToByteArray();

    private static byte[] OfType(Tkproto.TakionMessage.Types.PayloadType type)
        => new Tkproto.TakionMessage { Type = type }.ToByteArray();

    private static string? Read(string relativePath)
    {
        string? path = ChiakiNg.Session.SanitizerSource.LocateRelative(relativePath);

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>A bang the console accepted, keyed, and the one outcome that ends the wait.</summary>
    [Fact]
    public void AnAcceptedBangKeysTheSessionAndFinishesTheState()
    {
        var keying = new Keying();

        BangReading reading = BangHandler.Read(Bang(), earlyStreaminfoHeld: false, keying);

        output.WriteLine($"{reading.Outcome}, flags {reading.Flags}");

        Assert.Equal(BangOutcome.Keyed, reading.Outcome);
        Assert.Null(reading.Refusal);
        Assert.True(reading.Flags.Finished);
        Assert.False(reading.Flags.Failed);
        Assert.True(BangHandler.EndsTheWait(reading.Outcome));

        // And the console's own bytes went to the derivation, in that order.
        Assert.Equal(1, keying.Derived);
        Assert.Equal(1, keying.Keyed);
        Assert.Equal(PubKey(), keying.SawPubKey);
        Assert.Equal(Sig(), keying.SawSig);
    }

    /// <summary>The four refusals, each on its own, in the order the C tests them.</summary>
    [Fact]
    public void EachOfTheFourRefusalsIsReported()
    {
        var keying = new Keying();

        Assert.Equal(
            BangRefusal.VersionNotAccepted,
            BangHandler.Read(Bang(versionAccepted: false), false, keying).Refusal);

        Assert.Equal(
            BangRefusal.EncryptedKeyNotAccepted,
            BangHandler.Read(Bang(encryptedKeyAccepted: false), false, keying).Refusal);

        Assert.Equal(
            BangRefusal.NoEcdhPubKey,
            BangHandler.Read(Bang(pubKey: []), false, keying).Refusal);

        Assert.Equal(
            BangRefusal.NoEcdhSig,
            BangHandler.Read(Bang(sig: []), false, keying).Refusal);

        // None of the four reached the derivation.
        Assert.Equal(0, keying.Derived);
    }

    /// <summary>
    /// Two refusals at once report the FIRST, which is what the log a reader sees will say.
    ///
    /// The order is not cosmetic: a console that accepted neither the version nor the encrypted key
    /// is reported as the version, and somebody reading that log for the second reason is looking
    /// at the wrong half of the handshake.
    /// </summary>
    [Fact]
    public void TwoRefusalsAtOnceReportTheFirst()
        => Assert.Equal(
            BangRefusal.VersionNotAccepted,
            BangHandler.Read(
                Bang(versionAccepted: false, encryptedKeyAccepted: false), false, new Keying()).Refusal);

    /// <summary>The two keying failures are told apart, and the crypt only runs after a secret.</summary>
    [Fact]
    public void TheTwoKeyingFailuresAreDifferentRefusals()
    {
        var noSecret = new Keying(derives: false);
        var noCrypt = new Keying(keys: false);

        Assert.Equal(BangRefusal.DeriveFailed, BangHandler.Read(Bang(), false, noSecret).Refusal);
        Assert.Equal(BangRefusal.CryptFailed, BangHandler.Read(Bang(), false, noCrypt).Refusal);

        // A derivation that failed does not go on to start a crypt.
        Assert.Equal(0, noSecret.Keyed);
        Assert.Equal(1, noCrypt.Keyed);
    }

    /// <summary>
    /// PP365: A REFUSED BANG SETS A FLAG THE RUN NEVER READS.
    ///
    /// state_failed is written by three handlers and watched by no predicate, so a console that says
    /// no and a console that says nothing reach the run identically - one of them after the whole
    /// timeout. Reproduced rather than repaired, and asserted so a port cannot quietly grow the use
    /// the C does not have.
    /// </summary>
    [Fact]
    public void ARefusedBangSetsAFlagTheWaitDoesNotWatch()
    {
        BangReading reading = BangHandler.Read(Bang(versionAccepted: false), false, new Keying());

        Assert.Equal(BangOutcome.Refused, reading.Outcome);
        Assert.True(reading.Flags.Failed);

        // The flag is set and the wait is not over.
        Assert.False(StreamConnectionStates.WaitEnds(reading.Flags));
        Assert.False(BangHandler.EndsTheWait(reading.Outcome));
        Assert.Equal(StreamStep.Wait, StreamConnectionStates.Next(reading.Flags, waitTimedOut: false));

        // And after the timeout it is the same answer as a console that never spoke.
        Assert.Equal(StreamStep.Failed, StreamConnectionStates.Next(reading.Flags, waitTimedOut: true));
        Assert.Equal(StreamStep.Failed, StreamConnectionStates.Next(default, waitTimedOut: true));
    }

    /// <summary>A disconnect goes to its own handler and leaves both flags where they were.</summary>
    [Fact]
    public void ADisconnectGoesToItsOwnHandlerAndTouchesNeitherFlag()
    {
        BangReading reading = BangHandler.Read(
            OfType(Tkproto.TakionMessage.Types.PayloadType.Disconnect), false, new Keying());

        Assert.Equal(BangOutcome.ToDisconnect, reading.Outcome);
        Assert.Equal(default, reading.Flags);
    }

    /// <summary>
    /// The FIRST streaminfo is saved and a second is dropped, which is a fall-through and not a rule.
    ///
    /// The save is guarded on the buffer being empty and the guard's body returns; there is no else,
    /// so a second one lands in the warning below. What the C never says is that it discarded one.
    /// </summary>
    [Fact]
    public void TheFirstEarlyStreaminfoIsSavedAndASecondIsDropped()
    {
        byte[] streaminfo = OfType(Tkproto.TakionMessage.Types.PayloadType.Streaminfo);

        Assert.Equal(
            BangOutcome.SavedEarly,
            BangHandler.Read(streaminfo, earlyStreaminfoHeld: false, new Keying()).Outcome);

        Assert.Equal(
            BangOutcome.Unexpected,
            BangHandler.Read(streaminfo, earlyStreaminfoHeld: true, new Keying()).Outcome);
    }

    /// <summary>Any other type is warned about and dropped, with neither flag written.</summary>
    [Fact]
    public void AnyOtherTypeIsDroppedWithNeitherFlag()
    {
        BangReading reading = BangHandler.Read(
            OfType(Tkproto.TakionMessage.Types.PayloadType.Heartbeat), false, new Keying());

        Assert.Equal(BangOutcome.Unexpected, reading.Outcome);
        Assert.Equal(default, reading.Flags);
    }

    /// <summary>A protobuf that will not decode is the same: logged, and nothing decided.</summary>
    [Fact]
    public void AnUndecodableMessageTouchesNeitherFlag()
    {
        // Field 1, length-delimited, five bytes promised and none supplied.
        BangReading reading = BangHandler.Read([0x0a, 0x05], false, new Keying());

        Assert.Equal(BangOutcome.Undecodable, reading.Outcome);
        Assert.Equal(default, reading.Flags);
    }

    /// <summary>
    /// A field over the C's buffer fails the DECODE rather than being refused, and the two differ.
    ///
    /// chiaki_pb_decode_buf returns false where the field is longer than the buffer, which nanopb
    /// reports as a decode failure - so an over-long key leaves both flags alone, while a MISSING
    /// one sets state_failed. Two shapes of a bad key with two different effects on the state.
    /// </summary>
    [Theory]
    [InlineData(BangHandler.EcdhPubKeyMax + 1, BangHandler.EcdhSigMax)]
    [InlineData(BangHandler.EcdhPubKeyMax, BangHandler.EcdhSigMax + 1)]
    public void AnOversizedFieldIsUndecodableRatherThanRefused(int pubKeyLength, int sigLength)
    {
        var keying = new Keying();

        BangReading reading = BangHandler.Read(
            Bang(pubKey: PubKey(pubKeyLength), sig: Sig(sigLength)), false, keying);

        Assert.Equal(BangOutcome.Undecodable, reading.Outcome);
        Assert.Equal(default, reading.Flags);
        Assert.Equal(0, keying.Derived);

        // And exactly at the bound it is a bang like any other.
        Assert.Equal(
            BangOutcome.Keyed,
            BangHandler.Read(
                Bang(pubKey: PubKey(BangHandler.EcdhPubKeyMax), sig: Sig(BangHandler.EcdhSigMax)),
                false,
                new Keying()).Outcome);
    }

    /// <summary>
    /// THE DRIFT CHECKS: the order, the two early returns, the fall-through and the bound.
    /// </summary>
    [Fact]
    public void TheCsHandlerStillMakesTheDecisionsThisPortCopied()
    {
        if (Read(BangHandlerSource.RelativePath) is not { } source)
            return;

        string? body = BangHandlerSource.HandlerBody(source);
        Assert.NotNull(body);

        Assert.Equal(
            [
                "!msg.bang_payload.version_accepted",
                "!msg.bang_payload.encrypted_key_accepted",
                "!ecdh_pub_key_buf.size",
                "!ecdh_sig_buf.size",
            ],
            BangHandlerSource.RefusalOrderIn(body));

        Assert.True(
            BangHandlerSource.NotABangStillTouchesNeitherFlag(body),
            "a message that is not a bang now reaches a flag before the version test");

        Assert.True(
            BangHandlerSource.ASecondEarlyStreaminfoStillFallsThrough(body),
            "a second early streaminfo is no longer dropped by falling through to the warning");

        (int PubKey, int Sig)? sizes = BangHandlerSource.BufferSizesIn(body);

        output.WriteLine($"buffers {sizes}");

        Assert.Equal((BangHandler.EcdhPubKeyMax, BangHandler.EcdhSigMax), sizes);
    }

    /// <summary>And the helper that applies those bounds still fails the whole message.</summary>
    [Fact]
    public void TheDecodeHelperStillRefusesAnOversizedField()
    {
        if (Read(BangHandlerSource.DecodeRelativePath) is not { } source)
            return;

        Assert.True(
            BangHandlerSource.AnOversizedFieldStillFailsTheDecode(source),
            "an oversized field no longer zeroes the size and fails the decode");
    }
}
