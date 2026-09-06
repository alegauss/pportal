using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP749: the congestion packet's fifteen bytes, and the seam PP714's thread reports into.
///
/// Every other takion datagram this port sends had a managed writer and this one did not, so the
/// numbers the congestion thread computes reached nothing outside the test project.
/// </summary>
public class TakionCongestionTests(ITestOutputHelper output) : IDisposable
{
    private readonly UdpClient peer = new(new IPEndPoint(IPAddress.Loopback, 0));

    private IPEndPoint PeerEndPoint => (IPEndPoint)peer.Client.LocalEndPoint!;

    public void Dispose()
    {
        peer.Dispose();
        GC.SuppressFinalize(this);
    }

    private Thread AnswerHandshake(TakionHandshakeResponder responder)
    {
        var thread = new Thread(() =>
        {
            var from = new IPEndPoint(IPAddress.Loopback, 0);

            while (responder.State != TakionResponderState.Done)
            {
                byte[] datagram = peer.Receive(ref from);

                if (responder.Answer(datagram) is { } answer)
                    peer.Send(answer, answer.Length, from);
            }
        })
        {
            IsBackground = true,
            Name = "takion peer",
        };

        thread.Start();
        return thread;
    }

    /// <summary>THE FIFTEEN BYTES, field by field and at the C's own offsets.</summary>
    [Fact]
    public void ThePacketIsTheCsFifteenBytes()
    {
        Span<byte> datagram = stackalloc byte[TakionCongestion.PacketSize];
        TakionCongestion.Write(datagram, received: 0x1234, lost: 0x00ff, keyPos: 0xdead_beef_0000_0042);

        Assert.Equal(TakionCongestion.PacketType, datagram[0]);

        // word_0, which the C never assigns and this never takes.
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(datagram[1..]));

        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(datagram[3..]));
        Assert.Equal(0x00ff, BinaryPrimitives.ReadUInt16BigEndian(datagram[5..]));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(datagram[7..]));

        // Only the low half of the key position goes out.
        Assert.Equal(0x0000_0042u, BinaryPrimitives.ReadUInt32BigEndian(datagram[0xb..]));
    }

    /// <summary>A buffer of any other size is refused rather than written past.</summary>
    [Theory]
    [InlineData(TakionCongestion.PacketSize - 1)]
    [InlineData(TakionCongestion.PacketSize + 1)]
    public void AWrongSizedBufferIsRefused(int size)
        => Assert.Throws<ArgumentException>(() => TakionCongestion.Write(new byte[size], 0, 0, 0));

    /// <summary>
    /// A REPORT REACHES THE PEER, read off its socket rather than from the sink's counters.
    /// </summary>
    [Fact]
    public void AReportReachesThePeer()
    {
        var responder = new TakionHandshakeResponder(0x0000_5c5c, [.. Enumerable.Repeat((byte)0x63, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_1749);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var sink = new TakionCongestionSink(takion);
        sink.Send(new CongestionReport(0x0101, 0x0202));

        var from = new IPEndPoint(IPAddress.Loopback, 0);
        peer.Client.ReceiveTimeout = 5000;
        byte[] arrived = peer.Receive(ref from);

        output.WriteLine($"{arrived.Length} bytes, type {arrived[0]}");

        Assert.Equal(TakionCongestion.PacketSize, arrived.Length);
        Assert.Equal(TakionCongestion.PacketType, arrived[0]);
        Assert.Equal(0x0101, BinaryPrimitives.ReadUInt16BigEndian(arrived.AsSpan(3)));
        Assert.Equal(0x0202, BinaryPrimitives.ReadUInt16BigEndian(arrived.AsSpan(5)));

        Assert.Equal(1, sink.Sent);
        Assert.Equal(1, takion.CongestionSent);
        Assert.Equal(ChiakiError.Success, sink.Last);
    }

    /// <summary>
    /// And the thread PP714 wrote drives it, which is what the seam was for.
    ///
    /// The report the thread computes is the one that goes out - not a number this test made up.
    /// </summary>
    [Fact]
    public void TheCongestionThreadsOwnReportIsWhatGoesOut()
    {
        var responder = new TakionHandshakeResponder(0x0000_5d5d, [.. Enumerable.Repeat((byte)0x64, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_2749);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var stats = new ManagedPacketStats();
        stats.PushGeneration(received: 40, lost: 10);

        var sink = new TakionCongestionSink(takion);
        using var control = new ManagedCongestionControl(stats, sink, lossMax: 1.0);

        CongestionReport report = control.Tick();

        var from = new IPEndPoint(IPAddress.Loopback, 0);
        peer.Client.ReceiveTimeout = 5000;
        byte[] arrived = peer.Receive(ref from);

        output.WriteLine($"reported {report.Received} received, {report.Lost} lost");

        Assert.Equal(report.Received, BinaryPrimitives.ReadUInt16BigEndian(arrived.AsSpan(3)));
        Assert.Equal(report.Lost, BinaryPrimitives.ReadUInt16BigEndian(arrived.AsSpan(5)));
        Assert.Equal(1, sink.Sent);
    }

    /// <summary>A report before the handshake is refused, and nothing is sent.</summary>
    [Fact]
    public void AReportBeforeTheHandshakeIsRefused()
    {
        using var takion = new ManagedTakion(0x0000_3749);
        var sink = new TakionCongestionSink(takion);

        sink.Send(new CongestionReport(1, 2));

        Assert.Equal(ChiakiError.Uninitialized, sink.Last);
        Assert.Equal(0, sink.Sent);
        Assert.Equal(1, sink.Offered);
        Assert.Equal(0, takion.CongestionSent);
    }

    /// <summary>The two claims this port makes about the C, held where they were copied from.</summary>
    [Fact]
    public void TheCStillFormatsItThisWay()
    {
        if (TakionCongestionSource.LocateControl() is { } control)
        {
            Assert.True(
                TakionCongestionSource.TheFirstWordIsNeverAssigned(File.ReadAllText(control)),
                "congestioncontrol.c assigns word_0 now, so this port is sending a field the C fills");
        }

        if (TakionCongestionSource.LocateTakion() is { } takion)
        {
            Assert.True(
                TakionCongestionSource.TheOffsetsAreStillThese(File.ReadAllText(takion)),
                "the C's congestion format has moved a field");
        }
    }

    /// <summary>PP741: and the seam it fills is off the unreached list, with nothing replacing it.</summary>
    [Fact]
    public void TheCongestionSeamIsNoLongerUnreached()
    {
        IReadOnlyList<string> unreached = SeamReach.UnreachedIn(typeof(TakionCongestionSink).Assembly);

        output.WriteLine(string.Join(", ", unreached));

        Assert.DoesNotContain(nameof(ICongestionSink), unreached);
        Assert.Equal([.. SeamReach.Expected.Select(one => one.Interface).Order(StringComparer.Ordinal)], unreached);
    }
}
