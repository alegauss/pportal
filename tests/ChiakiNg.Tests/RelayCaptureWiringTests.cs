using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP616, under PP27: the three pieces composed, and the three ways a relay run differs.
///
/// PP613 built the relay, PP614 let a capture be pointed through one, PP615 let a capture keep what
/// it is given. A run through the relay changes all three together - who fills the capture, how much
/// of each datagram it keeps, and where the session points - and getting one of the three wrong
/// records something nobody can tell apart afterwards.
///
/// What is NOT here is a run. Starting a capture needs a console, and PP22's line about what only a
/// runner can answer applies to hardware too. These hold the wiring the run depends on.
/// </summary>
public class RelayCaptureWiringTests
{
    /// <summary>
    /// `--via relay` is a word, not an address, and it is recognised as one.
    ///
    /// A caller who typed a loopback address by hand would get the session pointed there and
    /// nothing else - no relay to forward, no wider keep, and a capture fed by a tap that is not
    /// seeing the traffic. The word is what makes the three move together.
    /// </summary>
    [Fact]
    public void TheRelayIsAskedForByWord()
    {
        Assert.True(ExchangeCapture.AsksForRelay("relay"));
        Assert.True(ExchangeCapture.AsksForRelay("RELAY"));
        Assert.True(ExchangeCapture.AsksForRelay("  relay "));

        Assert.False(ExchangeCapture.AsksForRelay("127.0.0.1"));
        Assert.False(ExchangeCapture.AsksForRelay(null));
        Assert.False(ExchangeCapture.AsksForRelay(""));
    }

    /// <summary>And an address still means that address, which is PP614's own behaviour.</summary>
    [Fact]
    public void AnAddressStillMeansThatAddress()
    {
        Assert.Equal("10.0.0.9", ExchangeCapture.ConnectAddress("192.168.1.40", "10.0.0.9"));
        Assert.Equal("192.168.1.40", ExchangeCapture.ConnectAddress("192.168.1.40", null));
    }

    /// <summary>
    /// A writer told not to install a tap does not install one, which is the half that would
    /// otherwise record every arrival twice.
    ///
    /// Once whole from the relay and once at eighteen bytes from the C, in one file that says
    /// nothing about which row is which - a capture that looks like twice the traffic at half the
    /// width.
    /// </summary>
    [Fact]
    public void AWriterWithoutATapDoesNotDoubleRecord()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp616-{Guid.NewGuid():N}.txt");

        try
        {
            using (var writer = new TakionCaptureWriter(
                path, () => 0, new TakionTimingCapture(keepBytes: TakionTimingCapture.WholeDatagramBytes),
                installTap: false))
            {
                // The caller is what offers, as a relay run does.
                Assert.True(writer.Capture.Offer([1, 2, 3, 4, 5], 0));
                Assert.Single(writer.Capture.Datagrams);
            }

            IReadOnlyList<CapturedDatagram>? read = TakionCaptureFile.Read(File.ReadAllText(path));

            Assert.NotNull(read);
            CapturedDatagram only = Assert.Single(read!);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, only.Head);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The width a relay run asks for keeps a full-sized datagram whole.
    ///
    /// An upper bound rather than an MTU read off an adapter: which interface a run used is not
    /// something a capture should carry.
    /// </summary>
    [Fact]
    public void TheRelayWidthKeepsAFullDatagram()
    {
        Assert.True(TakionTimingCapture.WholeDatagramBytes > 1500);

        var capture = new TakionTimingCapture(keepBytes: TakionTimingCapture.WholeDatagramBytes);
        byte[] datagram = [.. Enumerable.Range(0, 1472).Select(i => (byte)i)];

        Assert.True(capture.Offer(datagram, 0, datagram.Length));
        Assert.Equal(1472, capture.Datagrams[0].Head.Length);
    }

    /// <summary>
    /// And the host offers the word, so the composition is reachable from a command line.
    ///
    /// Read out of the flag list rather than by running a capture: the list is what PP306 holds
    /// against the dispatch, and a composition nothing can ask for is the shape PP569 and PP570
    /// each found once.
    /// </summary>
    [Fact]
    public void TheHostDocumentsTheWord()
    {
        Assert.Contains(
            HostCommandLine.Flags,
            f => f.Name == "--via" && f.Summary.Contains("relay", StringComparison.OrdinalIgnoreCase));
    }
}
