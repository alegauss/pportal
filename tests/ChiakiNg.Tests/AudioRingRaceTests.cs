using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP815: the ring under the two threads that actually reach it.
///
/// PP95 ported streamsession.cpp's ring and took the QMutex off, which was right - the ring had one
/// caller and no thread. PP797 made it two: AudioRingSink.Pcm writes from the takion receive thread,
/// through the AV arm and the decoder, while AudioPlayback's drain thread reads Fill and calls Read.
/// Nothing re-asked what the QMutex had answered.
///
/// WHAT THE RACE CORRUPTS IS CONTENT, WHICH IS WHY NO COUNTER SAW IT. A first draft of this file
/// asserted that Fill stays inside [0, Capacity] and it PASSED against the unlocked ring - the
/// counter does drift, and Write's own overflow branch reclamps it. Nothing is dropped, nothing
/// overflows and nothing is concealed; the bytes are counted written and counted drained, and only
/// what they CONTAIN is wrong. So the assertion has to read the bytes.
///
/// THE EVICTION IS WHAT MAKES IT REACHABLE. Write's overflow branch advances readPos - the READER's
/// own cursor - and then copies into the ring, while the reader is taking a span from that cursor.
/// That is the tear: one chunk holding the tail of one frame and the head of another.
///
/// SO EACH FRAME IS ONE REPEATED BYTE. Capacity is eight frames and a drain reads one, so readPos
/// stays frame-aligned and every chunk should be a single frame of a single value. A chunk carrying
/// two values cannot happen under a lock, and is what a listener hears as a click.
/// </summary>
public class AudioRingRaceTests(ITestOutputHelper output)
{
    /// <summary>The announced stream: 48 kHz stereo, a frame every ten milliseconds.</summary>
    private const int FrameBytes = 480 * 2 * sizeof(short);

    /// <summary>How many frames the writer pushes per attempt.</summary>
    private const int Frames = 20000;

    /// <summary>How many distinct frame values cycle, all non-zero so a gap is not one of them.</summary>
    private const int Values = 250;

    /// <summary>
    /// A CHUNK IS NEVER TORN ACROSS TWO FRAMES, whichever way the two threads interleave.
    ///
    /// The writer runs flat out so the ring is permanently overflowing, which is the state that puts
    /// Write's eviction inside the reader's span. Repeated until a tear is seen or the attempts run
    /// out, because a race has no deterministic failure and what makes this an assertion rather than
    /// a hope is volume.
    /// </summary>
    [Fact]
    public void AChunkIsNeverTornAcrossTwoFrames()
    {
        // Pre-built, so the writer's loop is the copy and nothing else - which is where the tear is.
        byte[][] frames = [.. Enumerable.Range(0, Values).Select(one =>
        {
            byte[] frame = new byte[FrameBytes];
            Array.Fill(frame, (byte)(1 + one));
            return frame;
        })];

        var tears = 0;
        var torn = string.Empty;
        long chunks = 0;

        for (var attempt = 0; attempt < 8 && tears == 0; attempt++)
        {
            var ring = new AudioRing(AudioRing.CapacityFor(FrameBytes));

            var writer = new Thread(() =>
            {
                for (var i = 0; i < Frames; i++)
                    ring.Write(frames[i % Values]);
            })
            {
                IsBackground = true,
                Name = "ring race writer",
            };

            writer.Start();

            while (writer.IsAlive || ring.Fill > 0)
            {
                byte[] chunk = ring.Read(FrameBytes);
                if (chunk.Length == 0)
                    continue;

                chunks++;

                byte first = chunk[0];
                foreach (byte one in chunk)
                {
                    if (one == first)
                        continue;

                    tears++;

                    if (torn.Length == 0)
                        torn = $"a {chunk.Length}-byte chunk held {first} and {one}";

                    break;
                }
            }

            Assert.True(writer.Join(TimeSpan.FromSeconds(30)), "the writer did not finish");
        }

        output.WriteLine(
            $"{chunks} chunk(s) read, {tears} torn"
                + (torn.Length > 0 ? $" - {torn}" : string.Empty));

        Assert.Equal(0, tears);
    }

    /// <summary>
    /// And the frame alignment the assertion above rests on, stated on one thread.
    ///
    /// Capacity is eight frames and a drain reads one, so readPos comes back to zero every eighth
    /// read and a chunk is a whole frame. If either multiplier moved, the test above would be
    /// comparing bytes across a boundary the ring is allowed to hand over, and would read a correct
    /// chunk as a tear.
    /// </summary>
    [Fact]
    public void ADrainReadsWholeFramesSoAMixedChunkIsOnlyEverATear()
    {
        var ring = new AudioRing(AudioRing.CapacityFor(FrameBytes));

        byte[] frame = new byte[FrameBytes];
        Array.Fill(frame, (byte)0x5A);

        Assert.Equal(FrameBytes * 8, ring.Capacity);

        for (var i = 0; i < 8; i++)
            Assert.False(ring.Write(frame), $"write {i} evicted inside a ring built for eight");

        for (var i = 0; i < 8; i++)
        {
            byte[] chunk = ring.Read(FrameBytes);

            Assert.Equal(FrameBytes, chunk.Length);
            Assert.All(chunk, one => Assert.Equal((byte)0x5A, one));
        }

        Assert.Equal(0, ring.Fill);
    }
}
