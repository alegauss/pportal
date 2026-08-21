using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP252: one switch, two subjects.
///
/// <see cref="AFailedLookupIsAdvertisedAsAnEmptyAddress"/> carries the task: the arm that finds the
/// local address is the one arm whose failure nothing reads.
/// </summary>
public class AddressDiscoveryTests
{
    /// <summary>Only the gateway arm can raise the external-address flag.</summary>
    [Fact]
    public void OnlyTheGatewayArmCanProduceAnExternalAddress()
    {
        Assert.True(AddressDiscovery.CanProduceAnExternalAddress(GatewayStatus.Found));
        Assert.False(AddressDiscovery.CanProduceAnExternalAddress(GatewayStatus.Unknown));
        Assert.False(AddressDiscovery.CanProduceAnExternalAddress(GatewayStatus.NotFound));
    }

    /// <summary>
    /// THE FINDING. The lookup failed, nothing read that, and what goes on the wire is what the
    /// allocation left behind.
    /// </summary>
    [Fact]
    public void AFailedLookupIsAdvertisedAsAnEmptyAddress()
    {
        DiscoveryResult result = AddressDiscovery.Discover(
            GatewayStatus.NotFound,
            localLookup: null,
            gatewayLanAddress: "",
            mappingAdded: false,
            upnpExternal: null,
            stunExternal: "203.0.113.9");

        Assert.False(result.LocalAddressKnown);
        Assert.Equal(AddressDiscovery.Unwritten, result.LocalAdvertised);

        // And the run is otherwise a success - STUN found the external address.
        Assert.Equal(AddressSource.Stun, result.Source);
    }

    /// <summary>A lookup that worked is advertised as what it found.</summary>
    [Fact]
    public void ALookupThatWorkedIsAdvertisedAsItself()
    {
        DiscoveryResult result = AddressDiscovery.Discover(
            GatewayStatus.NotFound, "192.168.1.40", "", false, null, "203.0.113.9");

        Assert.True(result.LocalAddressKnown);
        Assert.Equal("192.168.1.40", result.LocalAdvertised);
    }

    /// <summary>The gateway arm takes the LAN address, and asks for the external one only after a mapping.</summary>
    [Fact]
    public void TheExternalIsAskedForOnlyAfterAMapping()
    {
        DiscoveryResult mapped = AddressDiscovery.Discover(
            GatewayStatus.Found, null, "192.168.1.40", mappingAdded: true,
            upnpExternal: "203.0.113.1", stunExternal: "203.0.113.9");

        Assert.Equal(AddressSource.Upnp, mapped.Source);
        Assert.Equal("192.168.1.40", mapped.LocalAdvertised);

        // The mapping was refused, so UPnP is never asked and STUN answers instead.
        DiscoveryResult unmapped = AddressDiscovery.Discover(
            GatewayStatus.Found, null, "192.168.1.40", mappingAdded: false,
            upnpExternal: "203.0.113.1", stunExternal: "203.0.113.9");

        Assert.Equal(AddressSource.Stun, unmapped.Source);
    }

    /// <summary>STUN runs whenever the flag is still down - on the no-gateway arm, always.</summary>
    [Fact]
    public void StunRunsWheneverTheFlagIsStillDown()
    {
        foreach (GatewayStatus status in new[] { GatewayStatus.Unknown, GatewayStatus.NotFound })
            Assert.True(AddressDiscovery.StunRuns(status, mappingAdded: true, upnpExternal: "1.2.3.4"));

        Assert.False(AddressDiscovery.StunRuns(GatewayStatus.Found, true, "1.2.3.4"));
        Assert.True(AddressDiscovery.StunRuns(GatewayStatus.Found, false, "1.2.3.4"));
        Assert.True(AddressDiscovery.StunRuns(GatewayStatus.Found, true, null));
    }

    /// <summary>Neither produced one, which is a run that advertises no external address at all.</summary>
    [Fact]
    public void NeitherSourceIsAlsoAnOutcome()
    {
        DiscoveryResult result = AddressDiscovery.Discover(
            GatewayStatus.NotFound, "10.0.0.5", "", false, null, stunExternal: null);

        Assert.Equal(AddressSource.None, result.Source);
        Assert.True(result.LocalAddressKnown);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheDiscoveryIsStillTheCores()
    {
        string? file = AddressDiscoverySource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            AddressDiscoverySource.TheTwoArmsStillWriteDifferentThings(core),
            "the two arms still write different things");
        Assert.True(
            AddressDiscoverySource.TheLocalLookupsResultIsStillDiscarded(core),
            "the local lookup's result is still discarded");
        Assert.True(
            AddressDiscoverySource.TheExternalIsStillAskedOnlyAfterAMapping(core),
            "the external is still asked only after a mapping");
        Assert.True(
            AddressDiscoverySource.StunStillRunsOnTheFlagAlone(core),
            "and STUN still runs on the flag alone");

        Assert.True(
            AddressDiscoverySource.TheLocalAddressIsStillCopiedWhole(core),
            "the local address is still copied whole into the session");
        Assert.True(
            AddressDiscoverySource.TheThreeFieldsAreStillTheSameWidth(core),
            "and the three fields are still the same width");

        Assert.True(
            AddressDiscoverySource.TheCommentStillDescribesALaterMove(core),
            "the comment still describes a move that happens further down");
    }
}
