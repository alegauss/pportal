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
}
