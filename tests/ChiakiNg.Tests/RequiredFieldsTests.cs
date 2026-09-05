using ChiakiNg.Protocol;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP730: proto2's required, and the two generators that disagree about it.
///
/// PP25's pair is one .proto through nanopb for the C and Google.Protobuf for this project, and
/// PP729 leaned on the managed one to decide a bang. The managed one is LENIENT: it writes and
/// parses a message with every required field absent, where nanopb - which is what the console's
/// protocol is actually spoken with - refuses the same bytes.
///
/// THE FOUR BYTES ARE THE WHOLE CASE. 08 01 1A 00 is a TakionMessage of type BANG carrying an empty
/// bang payload. Before this, PP729 read it as a bang and answered state_failed; the C logs a decode
/// failure and leaves both flags alone, so its wait runs on. The two only ended in the same place
/// because PP365 proved state_failed is watched by nobody.
/// </summary>
public class RequiredFieldsTests(ITestOutputHelper output)
{
    /// <summary>The message the disagreement was found on, as its bytes.</summary>
    private static readonly byte[] BareBang = [0x08, 0x01, 0x1a, 0x00];

    private sealed class Never : IBangKeying
    {
        public int Asked { get; private set; }

        public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig)
        {
            Asked++;
            return true;
        }

        public bool InitCrypt() => true;
    }

    private static Tkproto.TakionMessage CompleteBang()
        => new()
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
            BangPayload = new Tkproto.BangPayload
            {
                ServerVersion = 12,
                Token = 7,
                VersionAccepted = true,
                EncryptedKeyAccepted = true,
                SessionKey = "sessionId4321",
                EcdhPubKey = ByteString.CopyFrom([1, 2, 3]),
                EcdhSig = ByteString.CopyFrom([4, 5, 6]),
            },
        };

    /// <summary>
    /// The four bytes really are what they are said to be: a BANG with an empty payload.
    ///
    /// Written as a literal because it is the artefact this task is about, and derived from the
    /// generator beside it so the literal cannot quietly stop being that message.
    /// </summary>
    [Fact]
    public void TheFourBytesAreABangWithAnEmptyPayload()
    {
        byte[] built = new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
            BangPayload = new Tkproto.BangPayload(),
        }.ToByteArray();

        output.WriteLine(Convert.ToHexString(built));

        Assert.Equal(BareBang, built);
    }

    /// <summary>
    /// THE DISAGREEMENT, as an assertion: the managed parser takes it and nanopb does not.
    ///
    /// Both halves are asserted, because the finding is the DIFFERENCE. A change to either generator
    /// that closed the gap would fail here and be read rather than assumed.
    /// </summary>
    [Fact]
    public void TheManagedParserTakesWhatNanopbRefuses()
    {
        var parsed = Tkproto.TakionMessage.Parser.ParseFrom(BareBang);

        Assert.Equal(Tkproto.TakionMessage.Types.PayloadType.Bang, parsed.Type);
        Assert.NotNull(parsed.BangPayload);
        Assert.False(parsed.BangPayload.HasServerVersion);

        Assert.Null(TakionMessages.DecodeWithNanopb(BareBang));
    }

    /// <summary>And the five it is missing are named, out of the descriptor rather than a list.</summary>
    [Fact]
    public void TheFiveMissingRequiredFieldsAreNamed()
    {
        IReadOnlyList<string> missing = RequiredFields.MissingIn(
            Tkproto.TakionMessage.Parser.ParseFrom(BareBang));

        output.WriteLine(string.Join(", ", missing));

        Assert.Equal(
            [
                "tkproto.BangPayload.server_version",
                "tkproto.BangPayload.token",
                "tkproto.BangPayload.encrypted_key_accepted",
                "tkproto.BangPayload.version_accepted",
                "tkproto.BangPayload.session_key",
            ],
            missing);
    }

    /// <summary>
    /// THE PAIR AGREES ONCE THE CHECK IS APPLIED: present here is accepted there, on every case.
    ///
    /// The equivalence rather than the two halves apart, because that is what the port needs to be
    /// true - a managed reader that answers the same question the console's decoder does.
    /// </summary>
    [Fact]
    public void WhatThisAcceptsIsWhatNanopbAccepts()
    {
        (string What, byte[] Bytes)[] cases =
        [
            ("a bang with an empty payload", BareBang),
            ("a complete bang", CompleteBang().ToByteArray()),
            ("a type and no payload at all",
                new Tkproto.TakionMessage
                {
                    Type = Tkproto.TakionMessage.Types.PayloadType.Heartbeat,
                }.ToByteArray()),
            ("a bang missing only its token",
                new Tkproto.TakionMessage
                {
                    Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
                    BangPayload = new Tkproto.BangPayload
                    {
                        ServerVersion = 12,
                        VersionAccepted = true,
                        EncryptedKeyAccepted = true,
                        SessionKey = "k",
                    },
                }.ToByteArray()),
        ];

        foreach ((string what, byte[] bytes) in cases)
        {
            bool here = RequiredFields.AllPresentIn(Tkproto.TakionMessage.Parser.ParseFrom(bytes));
            bool there = TakionMessages.DecodeWithNanopb(bytes) is not null;

            output.WriteLine($"{what}: here {here}, nanopb {there}");

            Assert.Equal(there, here);
        }
    }

    /// <summary>
    /// A payload that never arrived has no required fields to be missing, which is nanopb's rule.
    ///
    /// The check descends only into sub-messages that are PRESENT. Judging an absent optional
    /// payload would refuse every message in the protocol, since none of them carries all thirty.
    /// </summary>
    [Fact]
    public void AnAbsentPayloadIsNotJudged()
    {
        var heartbeat = new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Heartbeat,
        };

        Assert.Empty(RequiredFields.MissingIn(heartbeat));
        Assert.True(RequiredFields.AllPresentIn(heartbeat));
    }

    /// <summary>
    /// The requirement is read from the .proto, so a field made required upstream is covered.
    ///
    /// PP279's finding, which PP720 is also about: a hand-kept list guards only what somebody
    /// thought of. This asserts the descriptor really marks these five, so the reflection above is
    /// reading the schema rather than agreeing with a list written beside it.
    /// </summary>
    [Fact]
    public void TheDescriptorIsWhereRequiredComesFrom()
    {
        string[] required =
        [
            .. Tkproto.BangPayload.Descriptor.Fields.InFieldNumberOrder()
                .Where(one => one.IsRequired)
                .Select(one => one.Name),
        ];

        output.WriteLine(string.Join(", ", required));

        Assert.Equal(
            ["server_version", "token", "encrypted_key_accepted", "version_accepted", "session_key"],
            required);

        // And the ECDH pair is optional, which is why a bang without them is a REFUSAL and not a
        // decode failure - two different arms of PP729, and this is what keeps them apart.
        Assert.DoesNotContain(
            Tkproto.BangPayload.Descriptor.Fields.InFieldNumberOrder().Where(one => one.IsRequired),
            one => one.Name is "ecdh_pub_key" or "ecdh_sig");
    }

    /// <summary>
    /// PP729, CORRECTED: the four bytes are a decode failure now, not a refusal.
    ///
    /// Which is the whole point of the check. The C writes neither flag here, so its wait runs on;
    /// before this the port wrote state_failed and only PP365's dead flag hid the difference.
    /// </summary>
    [Fact]
    public void TheBangHandlerNowReadsThemAsUndecodable()
    {
        var keying = new Never();

        BangReading reading = BangHandler.Read(BareBang, earlyStreaminfoHeld: false, keying);

        output.WriteLine($"{reading.Outcome} / {reading.Refusal}");

        Assert.Equal(BangOutcome.Undecodable, reading.Outcome);
        Assert.Null(reading.Refusal);
        Assert.Equal(default, reading.Flags);
        Assert.Equal(0, keying.Asked);
    }

    /// <summary>
    /// And a bang that IS complete but refused still writes the flag, so the two arms stay apart.
    ///
    /// The other direction, and the one a fix like this can break: making everything undecodable
    /// would satisfy the assertion above and lose PP729's refusals entirely.
    /// </summary>
    [Fact]
    public void ACompleteBangThatIsRefusedStillWritesTheFlag()
    {
        Tkproto.TakionMessage refused = CompleteBang();
        refused.BangPayload.VersionAccepted = false;

        BangReading reading = BangHandler.Read(refused.ToByteArray(), false, new Never());

        Assert.Equal(BangOutcome.Refused, reading.Outcome);
        Assert.Equal(BangRefusal.VersionNotAccepted, reading.Refusal);
        Assert.True(reading.Flags.Failed);
    }
}
