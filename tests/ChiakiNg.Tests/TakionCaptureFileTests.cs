using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP512, under PP27: the capture as a file, written by driving lib/src's own emit.
///
/// Everything here runs without a console. What needs one is calling the writer where a stream
/// starts - and the file it leaves is what PP27's remaining half measures against.
/// </summary>
[Collection(nameof(MessageTapTests))]
public class TakionCaptureFileTests
{
    private static byte[] Head(int baseType)
    {
        var head = new byte[ChiakiMessageTap.TakionHeadBytes];
        head[0] = (byte)baseType;
        for (var i = 1; i < head.Length; i++)
            head[i] = (byte)(i + 0x40);

        return head;
    }

    private static TakionTimingCapture Filled()
    {
        var capture = new TakionTimingCapture();
        capture.Offer(Head(TakionDispatch.Video), 0);
        capture.Offer(Head(TakionDispatch.Audio), 16_000);
        capture.Offer(Head(TakionDispatch.Control), 33_500);
        return capture;
    }

    /// <summary>A capture written and read back is the same capture.</summary>
    [Fact]
    public void ACaptureRoundTrips()
    {
        TakionTimingCapture capture = Filled();

        IReadOnlyList<CapturedDatagram>? read = TakionCaptureFile.Read(TakionCaptureFile.Write(capture));

        Assert.NotNull(read);
        Assert.Equal(capture.Datagrams.Count, read.Count);

        for (var i = 0; i < read.Count; i++)
        {
            Assert.Equal(capture.Datagrams[i].ArrivalMicroseconds, read[i].ArrivalMicroseconds);
            Assert.Equal(capture.Datagrams[i].Length, read[i].Length);
            Assert.Equal(capture.Datagrams[i].BaseType, read[i].BaseType);
            Assert.Equal(capture.Datagrams[i].Head, read[i].Head);
        }
    }

    /// <summary>The first line says what the file is, and a file that does not is not one.</summary>
    [Fact]
    public void TheVersionLineIsWhatIdentifiesIt()
    {
        Assert.StartsWith(
            TakionCaptureFile.FormatVersion, TakionCaptureFile.Write(Filled()), StringComparison.Ordinal);

        Assert.Null(TakionCaptureFile.Read("chiaki-exchange-1\n"));
        Assert.Null(TakionCaptureFile.Read(string.Empty));
    }

    /// <summary>A malformed row is a refusal, not a partial read.</summary>
    [Theory]
    [InlineData("0\t18\t2")]
    [InlineData("nope\t18\t2\tAABB")]
    [InlineData("0\t18\t2\tZZ")]
    public void AMalformedRowRefusesTheWholeFile(string row)
        => Assert.Null(TakionCaptureFile.Read($"{TakionCaptureFile.FormatVersion}\n{row}\n"));

    /// <summary>An empty capture is a valid file with no rows.</summary>
    [Fact]
    public void AnEmptyCaptureIsAValidFile()
    {
        IReadOnlyList<CapturedDatagram>? read =
            TakionCaptureFile.Read(TakionCaptureFile.Write(new TakionTimingCapture()));

        Assert.NotNull(read);
        Assert.Empty(read);
    }

    /// <summary>
    /// A read-back capture is still decidable by the dispatch, which is the join that matters.
    ///
    /// A file whose heads the models could not read would be a session spent producing something
    /// nothing measures - the same check PP510 makes in memory, made again across the format.
    /// </summary>
    [Fact]
    public void AReadBackHeadIsStillEnoughForTheDispatch()
    {
        IReadOnlyList<CapturedDatagram>? read = TakionCaptureFile.Read(TakionCaptureFile.Write(Filled()));

        Assert.NotNull(read);

        foreach (CapturedDatagram datagram in read)
            Assert.Equal(datagram.BaseType, TakionDispatch.BaseTypeOf(datagram.Head));
    }

    /// <summary>
    /// THE WHOLE PATH: the C's emit fills a writer, and disposing it leaves the file on disk.
    ///
    /// Driven through chiaki_message_tap_emit, so what is exercised is the emit the receive thread
    /// will use rather than a managed stand-in for it.
    /// </summary>
    [Fact]
    public void TheEmitFillsAFileTheWriterLeavesBehind()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp512-{Guid.NewGuid():N}.txt");
        var clock = new long[] { 0 };

        try
        {
            using (new TakionCaptureWriter(path, () => clock[0]))
            {
                foreach (int baseType in (int[])[TakionDispatch.Video, TakionDispatch.Audio])
                {
                    ChiakiMessageTap.Emit(
                        ExchangeTapDirection.Received,
                        ChiakiMessageTap.TakionChannel,
                        (ushort)baseType,
                        Head(baseType));

                    clock[0] += 16_000;
                }

                Assert.False(File.Exists(path), "the file is written on dispose, not as it goes");
            }

            IReadOnlyList<CapturedDatagram>? read = TakionCaptureFile.Read(File.ReadAllText(path));

            Assert.NotNull(read);
            Assert.Equal(2, read.Count);
            Assert.Equal(TakionDispatch.Video, read[0].BaseType);
            Assert.Equal(16_000, read[1].ArrivalMicroseconds);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Disposing twice writes once and leaves the tap off.
    ///
    /// A session that ended badly disposes from two places often enough that this is worth having.
    /// </summary>
    [Fact]
    public void DisposingTwiceIsOneWrite()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp512-{Guid.NewGuid():N}.txt");

        try
        {
            var writer = new TakionCaptureWriter(path, () => 0);
            writer.Dispose();

            File.WriteAllText(path, "overwritten");
            writer.Dispose();

            Assert.Equal("overwritten", File.ReadAllText(path));
            Assert.False(ChiakiMessageTap.Active);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
