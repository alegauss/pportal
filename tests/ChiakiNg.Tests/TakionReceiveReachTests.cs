using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP601, under PP27: the receive loop is unreachable by design, and the way in is a socket.
///
/// §PP27's remaining half is timing the loop around the MAC gate against the C. It says no entry
/// point exposes takion's receive and leaves that reading as an absence. These say why it is not
/// one: the whole chain is file-local, and removing a `static` is the local patch to vendored C
/// that a non-goal refuses - PP593 put PP33 and PP30 in that rule's paragraph as the only lines it
/// does not reach, and PP27 is not one of them.
/// </summary>
public class TakionReceiveReachTests
{
    /// <summary>
    /// The whole chain is file-local, so there is nothing to call.
    ///
    /// All four rather than the first: a plan that exposed takion_handle_packet alone would still
    /// not run what the loop runs, because the work is in the three below it.
    /// </summary>
    [Fact]
    public void EveryHandlerInTheChainIsFileLocal()
    {
        if (TakionReceiveReach.LocateSource() is not { } path)
            return;

        IReadOnlyList<string> exposed =
            TakionReceiveReach.HandlersThatAreNotStatic(File.ReadAllText(path));

        Assert.True(
            exposed.Count == 0,
            "these are no longer static in takion.c, which is a local patch to the vendored C "
                + "unless the non-goal was narrowed in the same commit: " + string.Join(", ", exposed));

        Assert.Equal(4, TakionReceiveReach.Handlers.Count);
    }

    /// <summary>And the public header offers no way in either, which is the other half of unreachable.</summary>
    [Fact]
    public void TheHeaderExposesNoReceive()
    {
        if (TakionReceiveReach.LocateHeader() is not { } path)
            return;

        Assert.True(
            TakionReceiveReach.TheHeaderExposesNoReceive(File.ReadAllText(path)),
            "takion.h now names a receive handler, so PP27's blocker has moved - check whether that "
                + "arrived as a patch the non-goal forbids");
    }

    /// <summary>
    /// THE DOOR: connect still takes the caller's own socket.
    ///
    /// This is what makes PP27 workable without a patch. Takion does not create its socket - it is
    /// handed one - so a local pair can drive the real loop with recorded datagrams, supplying the
    /// socket and thread §PP27 says a capture lacks rather than going around them.
    /// </summary>
    [Fact]
    public void ConnectTakesTheCallersSocket()
    {
        if (TakionReceiveReach.LocateHeader() is not { } path)
            return;

        Assert.True(
            TakionReceiveReach.ConnectStillTakesTheCallersSocket(File.ReadAllText(path)),
            $"{TakionReceiveReach.TheOpenDoor} no longer takes a chiaki_socket_t*, so the only way "
                + "into the receive loop that needs no patch has closed");
    }

    /// <summary>
    /// PP27 is NOT a line the vendored-C rule exempts, which is what closes the obvious route.
    ///
    /// Joined to VendoredCRule rather than restated, so narrowing that rule to admit PP27 is a
    /// decision taken in one place and this follows it instead of contradicting it.
    /// </summary>
    [Fact]
    public void ThePatchRouteIsClosedForPP27()
    {
        Assert.DoesNotContain("PP27", VendoredCRule.LinesItDoesNotReach);

        // And the two that ARE exempt are the deletions, not this.
        Assert.Equal(["PP33", "PP30"], VendoredCRule.LinesItDoesNotReach);
    }

    /// <summary>
    /// And the readers see the shapes they are looking for, so neither check is green on a pattern
    /// that stopped matching.
    /// </summary>
    [Fact]
    public void TheReadersSeeWhatTheyLookFor()
    {
        Assert.Single(TakionReceiveReach.HandlersThatAreNotStatic(
            "void takion_handle_packet(ChiakiTakion *t, uint8_t *b, size_t n)\n"
                + "static void takion_handle_packet_message(void)\n"
                + "static void takion_handle_packet_message_data(void)\n"
                + "static void takion_handle_packet_message_data_ack(void)\n"));

        Assert.False(TakionReceiveReach.TheHeaderExposesNoReceive(
            "CHIAKI_EXPORT void takion_handle_packet(ChiakiTakion *t);"));

        Assert.False(TakionReceiveReach.ConnectStillTakesTheCallersSocket(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_connect(ChiakiTakion *t, ChiakiTakionConnectInfo *i);"));

        Assert.True(TakionReceiveReach.ConnectStillTakesTheCallersSocket(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_connect(ChiakiTakion *t, "
                + "ChiakiTakionConnectInfo *i, chiaki_socket_t *sock);"));
    }
}
