using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP812: the port's own emit site, which is what a non-relay capture records now.
///
/// PP511 put the emit in takion.c and PP783 moved the session off it, so --capture-datagrams wrote
/// a short file about the ctrl channel and said nothing about the traffic having gone elsewhere.
/// The managed dispatch is the same position the C emits from - every datagram the loop took,
/// before the intake and before the AV branch.
/// </summary>
public class ManagedDatagramTapTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>
    /// The tap is process-wide, as the C's is, so a test that left one installed would fill the
    /// next test's capture. Uninstalled here rather than trusted to each case.
    /// </summary>
    public void Dispose()
    {
        ManagedDatagramTap.Install(_ => { }).Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Nothing installed is nothing offered, and the dispatch pays one bool for it.</summary>
    [Fact]
    public void WithNothingInstalledTheTapIsInactive()
    {
        using (ManagedDatagramTap.Install(_ => { }))
        {
            Assert.True(ManagedDatagramTap.Active);
        }

        Assert.False(ManagedDatagramTap.Active);

        // And an offer to nobody is a no-op rather than a throw: the dispatch guards on Active, but
        // a caller that did not is not a crash on the receive thread.
        ManagedDatagramTap.Offer([1, 2, 3]);
    }

    /// <summary>What is offered arrives, and the count is the tap's own.</summary>
    [Fact]
    public void EveryOfferedDatagramReachesTheCallback()
    {
        var lengths = new List<int>();

        using (ManagedDatagramTap.Install(datagram => lengths.Add(datagram.Length)))
        {
            ManagedDatagramTap.Offer(new byte[7]);
            ManagedDatagramTap.Offer(new byte[29]);

            Assert.Equal(2, ManagedDatagramTap.Offered);
        }

        Assert.Equal([7, 29], lengths);
    }

    /// <summary>
    /// INSTALL REPLACES, and the handle it replaced no longer uninstalls anything.
    ///
    /// The C's tap has the same rule and PP511 states the consequence: two taps is one tap and a
    /// silence. What this holds is the half that is easy to get wrong - a first handle disposed
    /// after a second install must not take the second one down with it, because that is a capture
    /// that stops early with no line saying so.
    /// </summary>
    [Fact]
    public void AReplacedHandleDoesNotUninstallItsSuccessor()
    {
        var first = new List<int>();
        var second = new List<int>();

        IDisposable one = ManagedDatagramTap.Install(d => first.Add(d.Length));
        IDisposable two = ManagedDatagramTap.Install(d => second.Add(d.Length));

        // The first handle is stale now, and disposing it must leave the second installed.
        one.Dispose();

        Assert.True(ManagedDatagramTap.Active);
        ManagedDatagramTap.Offer(new byte[5]);

        two.Dispose();
        Assert.False(ManagedDatagramTap.Active);

        Assert.Empty(first);
        Assert.Equal([5], second);
    }

    /// <summary>
    /// PP812: AND THE TAKION'S DISPATCH FEEDS IT, which is the whole of what the flag needed.
    ///
    /// Driven over a real socket, because the claim is about the receive loop: a responder answers
    /// the handshake and then sends datagrams, and what the tap sees is what the loop took. The
    /// dispatch callback is asserted alongside it - the tap is an addition, and a tap that
    /// displaced the arrivals would break every session to record one.
    /// </summary>
    [Fact]
    public void TheTakionsDispatchOffersEveryDatagramToTheTap()
    {
        using var peer = new System.Net.Sockets.UdpClient(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        var responder = new TakionHandshakeResponder(0x0000_8120, [.. Enumerable.Repeat((byte)0x4d, 32)]);

        var answering = new Thread(() =>
        {
            var from = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0);

            while (responder.State != TakionResponderState.Done)
            {
                byte[] datagram = peer.Receive(ref from);

                if (responder.Answer(datagram) is { } answer)
                    peer.Send(answer, answer.Length, from);
            }

            for (var i = 0; i < 5; i++)
            {
                byte[] datagram = [0x00, (byte)i, 0x02, 0x03, 0x04];
                peer.Send(datagram, datagram.Length, from);
            }
        })
        {
            IsBackground = true,
            Name = "tap peer",
        };

        answering.Start();

        var dispatched = 0;
        var tapped = new List<int>();

        using var takion = new ManagedTakion(0x0000_8121, _ => dispatched++);

        Assert.Equal(
            ChiakiError.Success,
            takion.Connect(
                (System.Net.IPEndPoint)peer.Client.LocalEndPoint!, expectTimeoutMs: 2000).Error);

        answering.Join(TimeSpan.FromSeconds(5));

        using (ManagedDatagramTap.Install(datagram => tapped.Add(datagram.Length)))
        {
            takion.NextTimeoutMs = 300;
            takion.RunLoop(enableCrypt: true, iterationLimit: 12);
        }

        output.WriteLine($"{dispatched} dispatched, {tapped.Count} tapped: {string.Join(", ", tapped)}");

        // The handshake happens before the loop, so what the tap sees is the five that followed.
        Assert.NotEmpty(tapped);
        Assert.Equal(dispatched, tapped.Count);
        Assert.All(tapped, one => Assert.Equal(5, one));
    }
}
