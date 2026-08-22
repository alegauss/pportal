using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP260: a lease of zero, and an address that can only be v4.
///
/// <see cref="ARunThatDoesNotTearDownLeavesThemForever"/> carries the task.
/// </summary>
public class PortMappingTests
{
    /// <summary>
    /// THE FINDING. Nothing on the router will expire the forwarding, so the only thing that removes
    /// it is a teardown that runs.
    /// </summary>
    [Fact]
    public void ARunThatDoesNotTearDownLeavesThemForever()
    {
        Assert.Equal("0", PortMapping.Lease);
        Assert.Equal(MappingLifetime.TeardownOnly, PortMapping.Lifetime);

        // Both ports mapped, teardown never reached.
        Assert.Equal(2, PortMapping.LeftBehind(controlPortUsed: true, dataPortUsed: true));
        Assert.Equal(1, PortMapping.LeftBehind(true, false));
        Assert.Equal(0, PortMapping.LeftBehind(false, false));
    }

    /// <summary>And a delete that failed does not stop the teardown around it.</summary>
    [Fact]
    public void AFailedDeleteDoesNotStopTheTeardown()
        => Assert.False(PortMapping.AFailedDeleteStopsTeardown);

    /// <summary>
    /// The gateway path cannot answer with IPv6 - the buffer is a dotted quad's worth, and PP252
    /// prefers this path when a gateway is found.
    /// </summary>
    [Fact]
    public void TheGatewayPathCannotAnswerWithIpv6()
    {
        Assert.False(PortMapping.CanReturnIpv6(UpnpCall.ExternalAddress));
        Assert.Equal(16, PortMapping.ExternalAddressBuffer);

        // Which is well under what an address field holds.
        Assert.True(PortMapping.ExternalAddressBuffer < PunchAccept.AddressLength);

        // And PP252 reaches for this path first whenever a gateway was found.
        Assert.True(AddressDiscovery.CanProduceAnExternalAddress(GatewayStatus.Found));
    }

    /// <summary>Only the discovery bounds itself; the validation after it does not.</summary>
    [Fact]
    public void OnlyTheDiscoveryIsBounded()
    {
        Assert.True(PortMapping.IsBounded(UpnpCall.Discover));
        Assert.Equal(2000, PortMapping.DiscoverMs);

        foreach (UpnpCall call in Enum.GetValues<UpnpCall>().Where(c => c != UpnpCall.Discover))
            Assert.False(PortMapping.IsBounded(call));

        // Which is why the thread around them carries a longer one of its own.
        Assert.True(GatewayDiscovery.TimeoutMs > PortMapping.DiscoverMs);
    }

    /// <summary>The forwarding is one to one, so the delete finds it by the port already recorded.</summary>
    [Fact]
    public void TheForwardingIsOneToOne()
        => Assert.Equal((ushort)9295, PortMapping.ExternalFor(9295));

    /// <summary>The port buffers are the same exact fit PP244 measured elsewhere.</summary>
    [Fact]
    public void ThePortBuffersAreTheSameExactFit()
    {
        Assert.Equal(6, PortMapping.PortBuffer);
        Assert.Equal(ProbeSend.PortBuffer, PortMapping.PortBuffer);
        Assert.True(ProbeSend.PortFits(ushort.MaxValue));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheMappingsAreStillTheCores()
    {
        string? file = PortMappingSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(PortMappingSource.TheLeaseIsStillZero(core), "the lease is still zero");
        Assert.True(
            PortMappingSource.OnlyTheTeardownStillDeletesThem(core),
            "and only the teardown still deletes them");
        Assert.True(
            PortMappingSource.AFailedDeleteStillDoesNotStop(core),
            "a failed delete is still logged and stepped over");

        Assert.True(
            PortMappingSource.TheMappingIsStillOneToOne(core), "the mapping is still one to one");
        Assert.True(
            PortMappingSource.TheAddressBufferIsStillSixteen(core),
            "the external address buffer is still sixteen bytes");

        Assert.True(
            PortMappingSource.DiscoveryIsStillBoundedAndValidationIsNot(core),
            "discovery is still bounded and the validation after it is not");
        Assert.True(
            PortMappingSource.TheValidationStillDemandsAConnectedGateway(core),
            "and the validation still demands a connected gateway");

        Assert.True(
            PortMappingSource.ThePortBuffersAreStillExact(core), "the port buffers are still exact");
    }
}
