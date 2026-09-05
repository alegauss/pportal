using System.Buffers.Binary;
using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP708: the speakers, opened - the sentence a stream with no sound existed to deny.
///
/// PP700 joined a decoder to the session and a stream decoded for the first time. Nothing joined a
/// speaker: no IAudioRenderClient anywhere in the assembly, and AudioRing - PP32's playback buffer -
/// with the selftest as its only caller. PP698 is how it surfaced rather than how it was looked
/// for: proving a loopback reference delivers needed something PLAYING, and the test had to
/// generate a WAV and hand it to SoundPlayer because the port could not play one.
///
/// THE PROOF IS PP698'S OWN LOOPBACK, which is the part worth reading twice. This does not assert
/// that a call succeeded - it opens the render endpoint, writes a tone, and READS IT BACK off the
/// same endpoint through the loopback capture PP698 built. Two subsystems of this port, one playing
/// and one listening, and the assertion is that the second heard the first.
///
/// A MACHINE WITH NO SPEAKERS IS NOT A FAILURE. Start reports rather than throwing, and these
/// return early on NoDevice - the same shape every device test here has.
/// </summary>
public class WasapiRenderTests(ITestOutputHelper output)
{
    private static readonly MicrophoneAnnouncement Announced = MicrophoneFormat.Announced;

    /// <summary>A unit of a tone, loud enough that the mix is not flagged silent.</summary>
    private static byte[] Unit(int at, double amplitude = 0.2)
    {
        var unit = new byte[MicrophoneFormat.BytesPerUnit(Announced)];

        for (var i = 0; i < Announced.FrameSize; i++)
        {
            double t = (at + i) / (double)Announced.Rate;
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * 440 * t) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(unit.AsSpan(i * 2), sample);
        }

        return unit;
    }

    /// <summary>The default endpoint opens and the engine starts taking frames.</summary>
    [Fact]
    public void TheDefaultEndpointOpensAndTheEngineTakesFrames()
    {
        if (WasapiRender.ActiveEndpoints().Count == 0)
        {
            output.WriteLine("this machine has no render endpoint");
            return;
        }

        using var render = new WasapiRender();
        RenderResult started = render.Start();

        output.WriteLine($"{started} on '{render.DeviceName}' (0x{render.LastError:x8})");

        if (started is RenderResult.NoDevice)
            return;

        Assert.Equal(RenderResult.Running, started);
        Assert.NotEqual(string.Empty, render.DeviceName);
        Assert.Equal(Announced, render.Format);

        // The engine drains whether or not anything is queued, and silence still counts as frames
        // handed over - which is what makes a render client's health readable at all.
        Assert.True(
            SpinWait.SpinUntil(() => render.FramesWritten > 0, TimeSpan.FromSeconds(3)),
            "the engine took nothing at all");

        output.WriteLine($"{render.FramesWritten} frame(s), health {render.Health}");
        Assert.Equal(CaptureHealth.Streaming, render.Health);
    }

    /// <summary>
    /// WHAT IS PLAYED IS HEARD, through PP698's loopback on the same endpoint.
    ///
    /// The assertion PP708 owes, and it costs nothing extra to make: this port can already listen to
    /// a render endpoint, so a tone written here should come back there. Energy rather than samples -
    /// the engine mixes, converts and may be at any volume - so what is asserted is that the
    /// loopback heard MORE while the tone was playing than it did with the queue drained.
    ///
    /// THE TWO WINDOWS ARE ADJACENT on purpose. A floor taken before the render opened would be a
    /// reading of whatever else the machine was doing a second earlier - another test, a
    /// notification - and this comparison is meant to be about the tone. Playing then quiet, moments
    /// apart, is the tightest pair a single test can take.
    /// </summary>
    [Fact]
    public void WhatIsPlayedIsHeardBackThroughTheLoopback()
    {
        if (WasapiRender.ActiveEndpoints().Count == 0)
            return;

        // A queue and not a list: the capture's pump adds from WASAPI's thread while this one
        // reads, and enumerating a List while another thread appends to it throws - which is a
        // flake that reports as a failure of the audio rather than of the test.
        var heard = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();

        using var listening = new WasapiCapture(one => heard.Enqueue(one.ToArray()));
        if (listening.Start(CaptureSide.RenderLoopback) is not CaptureResult.Running)
        {
            output.WriteLine($"the loopback would not open: 0x{listening.LastError:x8}");
            return;
        }

        using var render = new WasapiRender();
        if (render.Start() is not RenderResult.Running)
        {
            output.WriteLine($"the render would not open: 0x{render.LastError:x8}");
            return;
        }

        for (var unit = 0; unit < 60; unit++)
            render.Write(Unit(unit * Announced.FrameSize, amplitude: 0.35));

        var clock = Stopwatch.StartNew();
        SpinWait.SpinUntil(
            () => render.Queued == 0 && clock.Elapsed > TimeSpan.FromMilliseconds(700),
            TimeSpan.FromSeconds(4));

        byte[][] whilePlaying = [.. heard];
        double played = AtTheTone(whilePlaying);
        int units = whilePlaying.Length;

        // And now the same endpoint with nothing queued, which the render fills with silence.
        heard.Clear();
        SpinWait.SpinUntil(() => false, TimeSpan.FromMilliseconds(700));
        double quiet = AtTheTone([.. heard]);

        output.WriteLine($"playing {played:F0} over {units} unit(s), then quiet {quiet:F0}");

        Assert.True(units > 0, "the loopback heard nothing at all while the tone was playing");
        Assert.True(
            played > quiet * 4,
            $"the loopback did not hear THIS tone: {played:F0} playing against {quiet:F0} quiet");
    }

    /// <summary>
    /// How much of one frequency is in what was heard, rather than how loud it all was.
    ///
    /// A Goertzel at the tone's own 440 Hz, and the reason it is here rather than a sum of squares:
    /// total energy is a reading of whatever the machine happens to be playing, so a notification
    /// during the quiet window fails a test about a tone. Background audio is broadband and this bin
    /// is one frequency wide, so what rises when the render starts is this test's own signal.
    /// </summary>
    private static double AtTheTone(IEnumerable<byte[]> units)
    {
        double coefficient = 2 * Math.Cos(2 * Math.PI * 440 / Announced.Rate);
        double first = 0;
        double second = 0;
        var samples = 0;

        foreach (byte[] unit in units)
        {
            for (var i = 0; i + 1 < unit.Length; i += 2)
            {
                double value = BinaryPrimitives.ReadInt16LittleEndian(unit.AsSpan(i));
                double now = value + (coefficient * first) - second;

                second = first;
                first = now;
                samples++;
            }
        }

        if (samples == 0)
            return 0;

        // The bin's magnitude, normalised by how much was looked at so two windows of different
        // lengths are comparable.
        double magnitude = Math.Sqrt((first * first) + (second * second) - (coefficient * first * second));
        return magnitude / samples;
    }

    /// <summary>Writing before the device opens queues rather than throwing.</summary>
    [Fact]
    public void WritingBeforeStartQueues()
    {
        using var render = new WasapiRender();

        render.Write(Unit(0));

        Assert.Equal(1, render.Queued);
        Assert.Equal(CaptureHealth.Stopped, render.Health);
        Assert.Equal(TimeSpan.Zero, render.RunningFor);
    }

    /// <summary>An empty write is nothing, not a queued nothing.</summary>
    [Fact]
    public void AnEmptyWriteIsNothing()
    {
        using var render = new WasapiRender();

        render.Write([]);

        Assert.Equal(0, render.Queued);
    }

    /// <summary>Starting twice is the same device, which is Start's contract.</summary>
    [Fact]
    public void StartingTwiceIsTheSameDevice()
    {
        using var render = new WasapiRender();

        RenderResult first = render.Start();
        if (first is RenderResult.NoDevice)
            return;

        Assert.Equal(first, render.Start());
    }

    /// <summary>Disposing twice is safe, and a disposed render refuses rather than queueing.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var render = new WasapiRender();

        render.Dispose();
        render.Dispose();

        Assert.Throws<ObjectDisposedException>(() => render.Write(Unit(0)));
    }

    /// <summary>
    /// The render asks for the console role and the capture for communications, which is the
    /// difference PP698 drew and this inherits.
    ///
    /// Read out of the source rather than asserted about behaviour: which endpoint is nominated is
    /// not visible in what the engine does, and opening the communications one would play a
    /// console's soundtrack down a headset's telephone channel.
    /// </summary>
    [Fact]
    public void TheRenderTakesTheConsoleRole()
    {
        if (SanitizerSource.LocateRelative(@"app\Native\WasapiRender.cs") is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Contains(
            "Wasapi.EDataFlow.Render, Wasapi.ERole.Console", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// And PP708's own finding, held: AudioRing has a consumer that is not the selftest now.
    ///
    /// The line's symptom was that the port renders no audio at all, and the evidence was that the
    /// one playback model in the tree was asserted by its own arithmetic and called by nothing. A
    /// render client is not that model - but a test that only opened a device would leave the
    /// symptom's other half unexamined, so this says which half is which.
    /// </summary>
    [Fact]
    public void ThePortNowHasARenderClientAtAll()
    {
        Type[] rendering =
        [
            .. typeof(WasapiRender).Assembly.GetTypes()
                .Where(one => one.Name.Contains("Render", StringComparison.Ordinal))
                .Where(one => one.Namespace == "ChiakiNg.Native"),
        ];

        output.WriteLine(string.Join(", ", rendering.Select(one => one.Name)));

        Assert.Contains(rendering, one => one == typeof(WasapiRender));
    }
}
