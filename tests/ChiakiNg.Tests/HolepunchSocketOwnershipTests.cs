using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP502, under PP340: who closes the two sockets the punch hands out.
///
/// Most of what is asserted here is an absence - the creator closes neither - so the checks are
/// written to fail if a close APPEARS, which is the direction this can move in.
/// </summary>
public class HolepunchSocketOwnershipTests
{
    /// <summary>Two sockets, two closers, and neither of them is the thing that made them.</summary>
    [Fact]
    public void EachSocketHasItsOwnCloserAndNeitherIsTheCreator()
    {
        Assert.Equal(2, HolepunchSocketOwnership.Owners.Count);

        Assert.Equal(SocketCloser.Rudp, HolepunchSocketOwnership.CloserFor(HolepunchPortType.Ctrl));
        Assert.Equal(SocketCloser.Takion, HolepunchSocketOwnership.CloserFor(HolepunchPortType.Data));

        // Distinct, which is what makes "two owners" a fact rather than a phrasing.
        Assert.Equal(2, HolepunchSocketOwnership.Owners.Select(o => o.Closer).Distinct().Count());
    }

    /// <summary>
    /// The flow's owner disposes neither, and the answer is the same for both channels.
    ///
    /// The reflex this exists to refuse: a class that obtains sockets and is therefore made
    /// IDisposable over them. Here that would close a handle another object holds a copy of.
    /// </summary>
    [Theory]
    [InlineData(HolepunchPortType.Ctrl)]
    [InlineData(HolepunchPortType.Data)]
    public void TheFlowOwnerDisposesNeither(HolepunchPortType port)
        => Assert.False(HolepunchSocketOwnership.TheFlowOwnerMayDispose(port));

    /// <summary>Both are handed over by value, which is the whole reason one close is enough.</summary>
    [Fact]
    public void BothAreHandedOverByValue()
        => Assert.All(HolepunchSocketOwnership.Owners, o => Assert.True(o.HandedOverByValue));

    /// <summary>
    /// THE ABSENCE: the holepunch teardown closes neither socket it created.
    ///
    /// Narrow on purpose. holepunch.c closes plenty of sockets - probes, candidates - so a check
    /// for "this function closes nothing" would be about the wrong thing. It is these two by name.
    /// </summary>
    [Fact]
    public void TheHolepunchTeardownClosesNeitherSocket()
    {
        if (HolepunchSocketOwnershipSource.LocateHolepunch() is not { } path)
            return;

        string fini = Assert.IsType<string>(
            HolepunchSocketOwnershipSource.FiniBody(File.ReadAllText(path)));

        // The fixture first: a body that failed to parse would pass the absence check vacuously.
        Assert.Contains("chiaki_stop_pipe_fini(&session->select_pipe);", fini, StringComparison.Ordinal);

        Assert.True(HolepunchSocketOwnershipSource.TheFiniClosesNeitherSocket(fini));
    }

    /// <summary>
    /// And the rudp still takes a copy of the ctrl handle and closes that copy.
    ///
    /// If the init ever stored the pointer, the release order in session.c would stop being a
    /// convention and become a lifetime - and the model above would be describing the wrong risk.
    /// </summary>
    [Fact]
    public void TheRudpCopiesTheHandleAndClosesIt()
    {
        if (HolepunchSocketOwnershipSource.LocateRudp() is not { } path)
            return;

        Assert.True(HolepunchSocketOwnershipSource.TheRudpCopiesTheHandleAndClosesIt(
            File.ReadAllText(path)));
    }

    /// <summary>And takion is still told to close the data socket it is handed.</summary>
    [Fact]
    public void TakionStillClosesTheDataSocket()
    {
        if (HolepunchSocketOwnershipSource.LocateStream() is not { } path)
            return;

        Assert.True(HolepunchSocketOwnershipSource.TakionStillClosesTheDataSocket(
            File.ReadAllText(path)));
    }

    /// <summary>
    /// The session's teardown still releases the rudp before the holepunch session, in that order.
    ///
    /// Two lines four apart, either of which could move without anything failing to build.
    /// </summary>
    [Fact]
    public void TheSessionReleasesTheRudpFirst()
    {
        if (HolepunchSocketOwnershipSource.LocateSession() is not { } path)
            return;

        Assert.True(HolepunchSocketOwnershipSource.TheRudpIsReleasedFirst(File.ReadAllText(path)));

        Assert.Equal(
            ["chiaki_rudp_fini", "chiaki_holepunch_session_fini"],
            HolepunchSocketOwnership.TeardownOrder);
    }

    /// <summary>
    /// THE RULE MADE UNAVAILABLE: the nine asks offer no way to close a socket, and the interface
    /// is not IDisposable.
    ///
    /// A comment saying "do not dispose these" is advice; an interface with no member that could is
    /// the rule. Asserted by reflection because the claim is about what the type does NOT have, and
    /// that is precisely what a later edit adds without noticing.
    ///
    /// Fini is not a close. The C's teardown of the holepunch session releases threads, mappings,
    /// pipes and locks and closes neither socket - which is what the source checks above hold.
    /// </summary>
    [Fact]
    public void TheNineAsksOfferNoWayToCloseASocket()
    {
        Type asks = typeof(IHolepunchSession);

        Assert.False(typeof(IDisposable).IsAssignableFrom(asks));

        string[] closers = [.. asks.GetMembers()
            .Select(m => m.Name)
            .Where(n => n.Contains("Close", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Dispose", StringComparison.OrdinalIgnoreCase))];

        Assert.Empty(closers);

        // And the one member that sounds like teardown is the one the C calls Fini, so the absence
        // above is not just a naming accident.
        Assert.Contains("Fini", asks.GetMembers().Select(m => m.Name));
    }

    /// <summary>
    /// And the flow's outcome hands the data socket out without taking responsibility for it.
    ///
    /// A run that reached the end reports the socket and a Fini count of zero; a run that failed
    /// reports one Fini and no socket. Neither closes anything, because it cannot.
    /// </summary>
    [Fact]
    public void TheFlowReportsTheSocketAndReleasesTheSessionOnly()
    {
        var connect = new HolepunchConnect(new StubSession(), _ => new object());
        HolepunchConnectOutcome ran = connect.Run();

        Assert.Null(ran.FailedAt);
        Assert.NotNull(ran.DataSocket);
        Assert.Equal(0, ran.FinisCalled);

        HolepunchConnectOutcome failed =
            new HolepunchConnect(new StubSession { OfferFails = true }, _ => new object()).Run();

        Assert.Equal(HolepunchStep.CreateOffer, failed.FailedAt);
        Assert.Equal(1, failed.FinisCalled);
        Assert.Null(failed.DataSocket);
    }

    /// <summary>The nine asks, answered.</summary>
    private sealed class StubSession : IHolepunchSession
    {
        public bool OfferFails { get; init; }

        public object GetSocket(HolepunchPortType type) => new();

        public object GetRegistInfo() => new();

        public ChiakiNg.Native.ChiakiError CreateOffer()
            => OfferFails ? ChiakiNg.Native.ChiakiError.Network : ChiakiNg.Native.ChiakiError.Success;

        public ChiakiNg.Native.ChiakiError PunchHole(HolepunchPortType type)
            => ChiakiNg.Native.ChiakiError.Success;

        public string GetSelectedAddress() => "203.0.113.7";

        public ushort GetCtrlPort() => 9295;

        public void Fini()
        {
        }
    }
}
