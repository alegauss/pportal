using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP234: which interface the console is told to reach.
///
/// The test that carries the task is <see cref="WirelessWinsOverEthernet"/>. The rule is not stated
/// anywhere in the core as a preference - it falls out of where the walk breaks - and it is the
/// opposite of what anybody tidying this code would assume.
/// </summary>
public class LocalAddressTests
{
    private static Adapter Wired(params string[] addresses)
        => new(AdapterKind.Ethernet, addresses);

    private static Adapter WiFi(params string[] addresses)
        => new(AdapterKind.Wireless, addresses);

    private static Adapter Other(params string[] addresses)
        => new(AdapterKind.Other, addresses);

    /// <summary>One adapter, one address, nothing to choose between.</summary>
    [Fact]
    public void OneAdapterIsItsAddress()
        => Assert.Equal("192.168.0.10", LocalAddress.Pick([Wired("192.168.0.10")]));

    /// <summary>Anything that is neither wireless nor ethernet is skipped whole.</summary>
    [Fact]
    public void AnythingElseIsSkipped()
    {
        Assert.Null(LocalAddress.Pick([Other("10.0.0.1")]));
        Assert.Equal("192.168.0.10", LocalAddress.Pick([Other("10.0.0.1"), Wired("192.168.0.10")]));
    }

    /// <summary>An empty address and the any-address are not addresses.</summary>
    [Fact]
    public void TheTwoUselessAddressesAreSkipped()
    {
        Assert.Null(LocalAddress.Pick([Wired("", "0.0.0.0")]));
        Assert.Equal("192.168.0.10", LocalAddress.Pick([Wired("", "0.0.0.0", "192.168.0.10")]));
    }

    /// <summary>
    /// THE RULE. Wireless wins - not because anything prefers it, but because finding one is what
    /// ends the walk, and finding an ethernet address does not.
    /// </summary>
    [Fact]
    public void WirelessWinsOverEthernet()
    {
        // Ethernet first: the walk goes on and the wireless address replaces it.
        Assert.Equal("10.0.0.5", LocalAddress.Pick([Wired("192.168.0.10"), WiFi("10.0.0.5")]));

        // Wireless first: the walk stops there and the ethernet adapter is never reached.
        Assert.Equal("10.0.0.5", LocalAddress.Pick([WiFi("10.0.0.5"), Wired("192.168.0.10")]));
    }

    /// <summary>
    /// And among ethernets the LAST one wins, for the same reason: nothing stops the walk, so each
    /// one overwrites the one before.
    /// </summary>
    [Fact]
    public void TheLastEthernetWins()
        => Assert.Equal(
            "192.168.3.3",
            LocalAddress.Pick([Wired("192.168.1.1"), Wired("192.168.2.2"), Wired("192.168.3.3")]));

    /// <summary>Where wireless does stop it, later adapters cannot overwrite anything.</summary>
    [Fact]
    public void NothingAfterAWirelessFindIsLookedAt()
        => Assert.Equal(
            "10.0.0.5",
            LocalAddress.Pick([WiFi("10.0.0.5"), Wired("192.168.9.9"), WiFi("10.0.0.6")]));

    /// <summary>A second address on the same adapter is never reached - the core breaks on the first.</summary>
    [Fact]
    public void OnlyTheFirstUsableAddressOnAnAdapterIsTaken()
        => Assert.Equal("192.168.0.10", LocalAddress.Pick([Wired("192.168.0.10", "192.168.0.11")]));

    /// <summary>A machine with nothing usable gets nothing, which the caller does not check.</summary>
    [Fact]
    public void NothingUsableIsNothing()
    {
        Assert.Null(LocalAddress.Pick([]));
        Assert.Null(LocalAddress.Pick([Wired(), WiFi("0.0.0.0")]));
    }

    /// <summary>
    /// The bound the signature offers and the body declines, carried so the port has it written
    /// down rather than rediscovering that nobody checked.
    /// </summary>
    [Fact]
    public void TheBoundIsCarriedEvenThoughTheCoreDeclinesIt()
    {
        // The candidate's address field in the core is 40 bytes; an IPv4 string fits with room,
        // which is why the missing check has never bitten.
        Assert.True(LocalAddress.Fits("192.168.100.200", 40));

        // strlen plus one, which is what the memcpy copies - so an exact fit needs the terminator.
        Assert.True(LocalAddress.Fits("1.2.3.4", 8));
        Assert.False(LocalAddress.Fits("1.2.3.4", 7));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void ThePickIsStillTheCores()
    {
        string? file = LocalAddressSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(LocalAddressSource.OnlyTwoKindsAreStillConsidered(core), "two kinds");
        Assert.True(LocalAddressSource.TheTwoUselessAddressesAreStillSkipped(core), "and two skips");
        Assert.True(
            LocalAddressSource.TheWalkStillEndsOnlyOnWireless(core),
            "the walk still ends only on a non-ethernet find");
        Assert.True(
            LocalAddressSource.TheBoundIsStillDeclined(core),
            "and the length is still a parameter nothing reads");
    }
}
