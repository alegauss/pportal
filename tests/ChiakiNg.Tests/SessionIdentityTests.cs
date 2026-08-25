using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP337, continuing PP293: the four things session_init decides before anything is on the network.
///
/// PP297's capture cannot judge any of them. A recording shows the request that went out, not the
/// constant that was copied into it - so these are asserted against session.c, and the two that are
/// constants are asserted against it byte for byte.
/// </summary>
public class SessionIdentityTests
{
    /// <summary>
    /// THE DEVICE ID IS MOSTLY NOT RANDOM: ten fixed bytes, sixteen random, six zero.
    ///
    /// A port that generated 32 random bytes would build something the console has no reason to
    /// accept, and nothing local would say so - the prefix is not a checksum and is validated
    /// nowhere on this side.
    /// </summary>
    [Fact]
    public void TheDeviceIdIsAPrefixSomeRandomAndSixZeroes()
    {
        byte[] did = SessionIdentity.NewDeviceId(n => [.. Enumerable.Repeat((byte)0xAB, n)]);

        Assert.Equal(32, did.Length);
        Assert.Equal(SessionIdentity.DeviceIdPrefix.ToArray(), did[..10]);
        Assert.Equal(Enumerable.Repeat((byte)0xAB, 16), did[10..26]);
        Assert.Equal(SessionIdentity.DeviceIdSuffix.ToArray(), did[26..]);
    }

    /// <summary>Sixteen of the thirty-two bytes are the only ones that vary.</summary>
    [Fact]
    public void SixteenBytesAreRandomAndTheRestAreNot()
    {
        Assert.Equal(16, SessionIdentity.DeviceIdRandomLength);

        byte[] one = SessionIdentity.NewDeviceId();
        byte[] two = SessionIdentity.NewDeviceId();

        Assert.NotEqual(one, two);
        Assert.Equal(one[..10], two[..10]);
        Assert.Equal(one[26..], two[26..]);
    }

    /// <summary>And the suffix is written rather than left, which is how a reused buffer stays clean.</summary>
    [Fact]
    public void TheSuffixIsWrittenAndNotLeft()
    {
        byte[] did = SessionIdentity.NewDeviceId(n => [.. Enumerable.Repeat((byte)0xFF, n)]);

        Assert.All(did[26..], b => Assert.Equal(0, b));
    }

    /// <summary>A middle of the wrong length is refused rather than truncated into the suffix.</summary>
    [Fact]
    public void AMiddleOfTheWrongLengthIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SessionIdentity.NewDeviceId(_ => new byte[4]));
    }

    /// <summary>
    /// A COLON IN THE HOSTNAME IS WHAT CHOOSES IPv6, and the family is pinned either way.
    ///
    /// "make hostname use ipv4 for now" is session.c's own comment. A name that resolves to both is
    /// therefore resolved as v4 unless it was written as a v6 literal.
    /// </summary>
    [Theory]
    [InlineData("192.168.1.224", AddressFamily.InterNetwork)]
    [InlineData("ps5.local", AddressFamily.InterNetwork)]
    [InlineData("fd00::1", AddressFamily.InterNetworkV6)]
    [InlineData("::1", AddressFamily.InterNetworkV6)]
    public void AColonChoosesIpv6AndNothingElseDoes(string host, AddressFamily family)
    {
        Assert.Equal(family, SessionIdentity.FamilyFor(host));
    }

    /// <summary>The target comes from the family flag and nothing else.</summary>
    [Fact]
    public void TheTargetComesFromTheFamilyFlag()
    {
        Assert.Equal(ChiakiTarget.Ps5_1, SessionIdentity.TargetFor(ps5: true));
        Assert.Equal(ChiakiTarget.Ps4_10, SessionIdentity.TargetFor(ps5: false));
    }

    /// <summary>
    /// DISABLING VIDEO STREAMS 360p, which is not the same as streaming nothing.
    ///
    /// The preset is replaced with the smallest one at fps zero; the stream is still negotiated. A
    /// port reading the flag as "send no video" would produce a session the console ends.
    /// </summary>
    [Fact]
    public void DisablingVideoAsksForTheSmallestPresetRatherThanForNone()
    {
        Assert.Equal(ChiakiVideoResolution.P360, SessionIdentity.DisabledVideoPreset.Resolution);
        Assert.Equal(0, (int)SessionIdentity.DisabledVideoPreset.Fps);
    }

    /// <summary>And session.c still decides all four the way this does.</summary>
    [Fact]
    public void SessionStillDeclaresTheFour()
    {
        string? core = SessionIdentitySource.Locate();
        string? header = SessionIdentitySource.LocateHeader();
        if (core is null || header is null)
            return;

        string source = File.ReadAllText(core);

        Assert.True(
            SessionIdentitySource.TheDeviceIdIsStill(
                File.ReadAllText(header), SessionIdentity.DeviceIdSize),
            "CHIAKI_RP_DID_SIZE has changed");
        Assert.True(
            SessionIdentitySource.TheFixedBytesAreStill(
                source, SessionIdentity.DeviceIdPrefix, SessionIdentity.DeviceIdSuffix),
            "the device id's fixed bytes have changed");
        Assert.True(
            SessionIdentitySource.TheMiddleIsStillCryptoRandom(source),
            "the device id's middle no longer comes from the crypto random source");
        Assert.True(
            SessionIdentitySource.AColonStillChoosesIpv6(source),
            "the address family is no longer chosen by a colon in the hostname");
        Assert.True(
            SessionIdentitySource.DisabledVideoIsStill360p(source),
            "disabling video no longer asks for 360p");
    }
}
