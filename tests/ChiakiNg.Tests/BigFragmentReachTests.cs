using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP827: whether this port's BIG is long enough to need the fragmentation it does not do.
///
/// streamconnection.c splits the BIG against the MTU and this port sends it in one call, so the
/// question is a LENGTH before it is a loop: under the MTU the C's own loop runs zero times and the
/// two behave identically, which is why a stream has worked at all.
/// </summary>
public class BigFragmentReachTests(ITestOutputHelper output)
{
    private static readonly byte[] HandshakeKey = [.. Enumerable.Range(0, 16).Select(one => (byte)one)];
    private static readonly byte[] Nonce = [.. Enumerable.Range(0, 16).Select(one => (byte)(0x40 + one))];
    private static readonly byte[] Morning = [.. Enumerable.Range(0, 16).Select(one => (byte)(0x20 + one))];
    private static readonly byte[] PubKey = [.. Enumerable.Range(0, 65).Select(one => (byte)one)];
    private static readonly byte[] Sig = [.. Enumerable.Range(0, 32).Select(one => (byte)(0x80 + one))];

    /// <summary>A BIG the port would actually send, at the profile a PS5 session carries.</summary>
    private static byte[] Big(int mtu)
    {
        using var crypt = new RpCrypt(ChiakiTarget.Ps5Unknown, Nonce, Morning);

        var fields = new LaunchSpecFields(
            1920, 1080, 60, 15000, (uint)mtu, 12, ChiakiTarget.Ps5Unknown, ChiakiCodec.H265Hdr);

        string spec = BigMessage.EncodedLaunchSpec(crypt, fields, HandshakeKey)!;

        return BigMessage.Encode(
            clientVersion: 9,
            sessionKey: "0123456789abcdef0123456789abcdef",
            encodedLaunchSpec: spec,
            ecdhPubKey: PubKey,
            ecdhSig: Sig);
    }

    /// <summary>
    /// AT SENKUSHA'S NARROWEST MTU THE C SENDS THREE MESSAGES AND THIS PORT SENDS ONE.
    ///
    /// The MTU here is senkusha's, already reduced by the fifty bytes of network overhead it takes
    /// off before any of this - the number BigFragments.Plan expects and the one the C passes. 576
    /// is the narrowest its search can report, and a link that measures there gets an oversized
    /// datagram from this port where the C would fragment.
    /// </summary>
    [Fact]
    public void AtTheNarrowestMtuTheBigNeedsMoreThanOneMessage()
    {
        byte[] body = Big(BigFragments.NarrowestMeasuredMtu);

        int usable = BigFragments.NarrowestMeasuredMtu - BigFragments.NetworkOverhead;
        IReadOnlyList<BigFragment> plan = BigFragments.Plan(body.Length, usable);

        output.WriteLine($"usable {usable}: BIG is {body.Length} bytes over {plan.Count} message(s)");

        foreach (BigFragment one in plan)
            output.WriteLine($"  offset {one.Offset,5} size {one.Size,5} first {one.IsFirst} ends {one.EndsTheMessage}");

        Assert.True(plan.Count > 1, $"the BIG fits one message at {usable}, so this line would be a retirement");

        // Exactly one fragment ends the message and it is the last, which is PP376's invariant and
        // the half this port would have to get right along with the loop.
        Assert.Single(plan, one => one.EndsTheMessage);
        Assert.True(plan[^1].EndsTheMessage);
    }

    /// <summary>
    /// AND AT THE FALLBACK MTU IT FITS BY SEVEN BYTES, which is why nothing has broken yet.
    ///
    /// 1454 is what a session uses when senkusha's measurement fails outright, and on that link the
    /// C's loop runs zero times - so the port's single send is byte for byte the C's, and every
    /// working stream is explained. What the margin says is that the explanation is luck: a longer
    /// session key or another profile string moves the BIG past it, and then the DEFAULT path is
    /// sending an oversized datagram too.
    ///
    /// The seven is not asserted as a constant. What is held is that it fits AND that the headroom
    /// is under one fragment's worth, because a number written down here would rot the first time
    /// the launch spec gained a field.
    /// </summary>
    [Fact]
    public void AtTheFallbackMtuItFitsWithAlmostNothingToSpare()
    {
        const int Fallback = 1454;

        byte[] body = Big(Fallback);

        int usable = Fallback - BigFragments.NetworkOverhead;
        int headroom = usable - (body.Length + BigFragments.FirstOverhead);

        output.WriteLine($"usable {usable}: BIG is {body.Length} bytes, headroom {headroom}");

        Assert.Single(BigFragments.Plan(body.Length, usable));

        Assert.InRange(headroom, 0, 64);
    }

    /// <summary>
    /// PP827: AND THE SINK NOW CUTS IT, which is the half the measurement above asked for.
    ///
    /// Over a real socket, because the claim is about datagrams: at the narrowest MTU the BIG leaves
    /// as three, none of them larger than the MTU, and exactly the last carries the end-of-message
    /// flag. Before this the sink called SendData once and one oversized datagram went out.
    /// </summary>
    [Fact]
    public void TheSinkSendsTheBigAsTheFragmentsThePlanNames()
    {
        const uint TagRemote = 0x71DC_4009;

        var peer = new System.Net.Sockets.UdpClient(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        using (peer)
        {
            var responder = new TakionHandshakeResponder(TagRemote, [.. Enumerable.Repeat((byte)0x71, 32)]);

            var answering = new Thread(() =>
            {
                var from = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0);

                while (responder.State != TakionResponderState.Done)
                {
                    byte[] datagram = peer.Receive(ref from);

                    if (responder.Answer(datagram) is { } answer)
                        peer.Send(answer, answer.Length, from);
                }
            })
            {
                IsBackground = true,
                Name = "big peer",
            };

            answering.Start();

            using var takion = new ManagedTakion(0x0000_8270);

            Assert.Equal(
                ChiakiError.Success,
                takion.Connect(
                    (System.Net.IPEndPoint)peer.Client.LocalEndPoint!, expectTimeoutMs: 2000).Error);

            answering.Join(TimeSpan.FromSeconds(5));
            peer.Client.ReceiveTimeout = 2000;

            int usable = BigFragments.NarrowestMeasuredMtu - BigFragments.NetworkOverhead;
            byte[] body = Big(BigFragments.NarrowestMeasuredMtu);

            var sink = new TakionMessageSink(takion) { Mtu = usable };

            Assert.True(
                sink.SendBig(new StreamMessage(StreamMessages.OrdinaryData, PayloadType: 0, body)));

            IReadOnlyList<BigFragment> plan = BigFragments.Plan(body.Length, usable);
            Assert.Equal(plan.Count, sink.Sent);

            // The first is a data message and the rest are continuations, which is the C's two
            // exports and the reason ManagedTakion needed a second send at all.
            Assert.Equal(1, takion.DataSent);
            Assert.Equal(plan.Count - 1, takion.ContinuationSent);

            var from = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0);

            for (var i = 0; i < plan.Count; i++)
            {
                byte[] onTheWire = peer.Receive(ref from);

                output.WriteLine($"fragment {i}: {onTheWire.Length} bytes against a {usable} MTU");

                Assert.True(
                    onTheWire.Length <= usable,
                    $"fragment {i} is {onTheWire.Length} bytes, which is over the {usable} MTU");
            }
        }
    }
}
