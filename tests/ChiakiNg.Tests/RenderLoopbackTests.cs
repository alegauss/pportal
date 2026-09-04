using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// One loopback reference, opened once for the whole class.
///
/// PP698: the default render endpoint first, because that is what a host opens, then the rest -
/// the same order and the same reason PP652's microphone fixture has. What differs is what
/// "streaming" means: a render endpoint playing nothing produces NO packets, so an endpoint that
/// opens and hands over nothing is the ordinary state of a quiet machine rather than a failure.
///
/// So this waits, records what happened, and asserts nothing. The tests decide.
/// </summary>
public sealed class RenderLoopback : IDisposable
{
    /// <summary>How long the reference is given to produce something before it is called quiet.</summary>
    public static TimeSpan Listen { get; } = TimeSpan.FromSeconds(3);

    private WasapiCapture? capture;
    private int units;
    private int wrongSize;

    /// <summary>What Start reported for the endpoint that opened, or NoDevice where none did.</summary>
    public CaptureResult Started { get; private set; } = CaptureResult.NoDevice;

    /// <summary>The endpoint's name, once one is open.</summary>
    public string DeviceName => capture?.DeviceName ?? string.Empty;

    /// <summary>Units the sink has been handed. Zero on a machine playing nothing.</summary>
    public int Units => Volatile.Read(ref units);

    /// <summary>Units whose length was not the announced one, which must be none.</summary>
    public int WrongSize => Volatile.Read(ref wrongSize);

    /// <summary>Every endpoint tried, with what it did.</summary>
    public IReadOnlyList<string> Attempts => attempts;

    private readonly List<string> attempts = [];

    /// <summary>The capture, for a test that needs its health.</summary>
    public WasapiCapture? Capture => capture;

    public RenderLoopback()
    {
        var order = new List<string?> { null };
        order.AddRange(WasapiCapture.ActiveRenderEndpoints().Select(one => (string?)one.Id));

        foreach (string? id in order)
        {
            if (Open(id))
                return;
        }
    }

    private bool Open(string? id)
    {
        int expected = MicrophoneFormat.BytesPerUnit(MicrophoneFormat.Announced);

        var opening = new WasapiCapture(one =>
        {
            if (one.Length != expected)
                Interlocked.Increment(ref wrongSize);

            Interlocked.Increment(ref units);
        });

        CaptureResult started = opening.Start(CaptureSide.RenderLoopback, id);

        if (started is not CaptureResult.Running)
        {
            attempts.Add($"{id ?? "(default)"}: {started} 0x{opening.LastError:x8}");
            opening.Dispose();
            return false;
        }

        // The reference is kept whether or not it speaks: a quiet machine is the case this line is
        // most about, and dropping the endpoint here would leave nothing to judge.
        var clock = Stopwatch.StartNew();
        bool spoke = SpinWait.SpinUntil(() => Volatile.Read(ref units) >= 1, Listen);
        clock.Stop();

        attempts.Add(
            $"'{opening.DeviceName}': "
                + (spoke ? $"heard something in {clock.ElapsedMilliseconds} ms" : "quiet"));

        capture = opening;
        Started = started;
        return true;
    }

    public void Dispose() => capture?.Dispose();
}

/// <summary>
/// PP698, under PP52: a loopback client on the render endpoint, which is the echo canceller's
/// second input.
///
/// spike/audio-effects measured the shape of the requirement: in filter mode the Voice Capture DSP
/// declares two inputs and one output - the microphone, and a reference of what is being played.
/// PP652 built the first and there was no second, because WasapiCapture opened capture endpoints
/// only.
///
/// THE INTEROP IS THE ONE THAT EXISTS, which is the finding rather than a shortcut. A loopback
/// client is the same IAudioClient on a RENDER endpoint with one more flag, and the capture service
/// under it is the same interface - so the reference arrives through the same converter, in the same
/// announced format, as whole units of the same size. A second class would have been a second copy
/// of a COM surface PP693 already had to make a rule about.
///
/// AND SILENCE IS DIFFERENT ON THIS SIDE. A microphone in a quiet room delivers zeroes; a render
/// endpoint playing nothing delivers NO PACKETS AT ALL. That is what Windows documents and it is
/// the state PP695 taught this port to notice, so the reference reads as Silent rather than as a
/// working stream - which is the difference between a canceller with one input and a canceller that
/// thinks it has two.
/// </summary>
public class RenderLoopbackTests(ITestOutputHelper output, RenderLoopback reference)
    : IClassFixture<RenderLoopback>
{
    /// <summary>
    /// THE ONE THAT MATTERS: a render endpoint opens as a capture, which only the flag allows.
    ///
    /// Activating an audio client on a render endpoint and asking it for a capture service fails
    /// without AUDCLNT_STREAMFLAGS_LOOPBACK - a render endpoint has no capture service to give. So
    /// this opening at all is the assertion, and the HRESULT is printed either way.
    /// </summary>
    [Fact]
    public void TheDefaultRenderEndpointOpensAsALoopbackCapture()
    {
        foreach (string attempt in reference.Attempts)
            output.WriteLine(attempt);

        output.WriteLine($"{reference.Started} on '{reference.DeviceName}'");

        // A machine with no render endpoint at all says nothing about this code.
        if (WasapiCapture.ActiveRenderEndpoints().Count == 0)
            return;

        Assert.True(
            reference.Started is CaptureResult.Running,
            "no active render endpoint on this machine opened as a loopback capture:\n  "
                + string.Join("\n  ", reference.Attempts));

        Assert.NotEqual(string.Empty, reference.DeviceName);
        Assert.Equal(CaptureSide.RenderLoopback, reference.Capture!.Side);
    }

    /// <summary>
    /// PP695's state, on the side it is the ordinary answer for.
    ///
    /// A quiet machine is not a broken one, and this is the case the criterion names: the reference
    /// must read as SILENT rather than as a working stream, because a canceller handed a reference
    /// that never arrives is subtracting nothing from something and reporting success.
    ///
    /// Both outcomes are legitimate here - a machine playing music reads as Streaming, and so does
    /// this one while the tone below is playing into the endpoint the fixture is reading. So the
    /// claim is what the health can never be: Starting, once the grace period is past, or Streaming
    /// with no unit behind it.
    ///
    /// The health is read ONCE and the counter after it, which is what makes that race-free: the
    /// counter never falls, so Streaming implies it was already non-zero, while Silent only says it
    /// was zero at the moment of the reading.
    /// </summary>
    [Fact]
    public void TheReferenceReportsWhatItHeardRatherThanThatItOpened()
    {
        if (reference.Capture is not { } capture || reference.Started is not CaptureResult.Running)
            return;

        CaptureHealth health = capture.Health;
        long delivered = capture.UnitsCaptured;

        output.WriteLine(
            $"{health} after {capture.RunningFor.TotalMilliseconds:F0} ms with {delivered} unit(s)");

        Assert.True(
            capture.RunningFor > CaptureSilence.Grace,
            "the fixture listened for less than the grace period, so the health is still Starting");

        Assert.NotEqual(CaptureHealth.Starting, health);
        Assert.NotEqual(CaptureHealth.Stopped, health);

        if (health == CaptureHealth.Streaming)
            Assert.True(delivered >= 1, "Streaming with nothing behind it");
    }

    /// <summary>
    /// And whatever DID arrive is a whole unit of the announced size, which is the format claim.
    ///
    /// The reference has to be in the same units as the microphone or the subtraction has nothing
    /// to line up. It is, because both sides go through the same converter and the same accumulator
    /// - so this asserts the consequence rather than the arrangement, and says nothing at all on a
    /// machine that was quiet.
    /// </summary>
    [Fact]
    public void EveryUnitTheReferenceProducedIsTheAnnouncedSize()
    {
        output.WriteLine($"{reference.Units} unit(s), {reference.WrongSize} of the wrong size");

        Assert.Equal(0, reference.WrongSize);

        if (reference.Capture is { } capture && reference.Units > 0)
            Assert.True(capture.UnitsCaptured >= reference.Units);
    }

    /// <summary>
    /// The flag is the one Windows documents, named rather than inlined.
    ///
    /// The same discipline PP652 applied to AUTOCONVERTPCM: if this number changed, the client would
    /// still be initialised - with a flag that means something else - and the failure would be an
    /// HRESULT nobody could place.
    /// </summary>
    [Fact]
    public void TheLoopbackFlagIsTheOneWindowsDocuments()
    {
        Assert.Equal(0x00020000, WasapiCapture.AUDCLNT_STREAMFLAGS_LOOPBACK);

        // And it is a third flag rather than a replacement: the conversion is what puts the
        // reference in the microphone's units, so dropping either leaves the other useless.
        Assert.NotEqual(
            WasapiCapture.AUDCLNT_STREAMFLAGS_LOOPBACK,
            WasapiCapture.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM);

        Assert.NotEqual(
            WasapiCapture.AUDCLNT_STREAMFLAGS_LOOPBACK,
            WasapiCapture.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY);
    }

    /// <summary>
    /// A render endpoint is not a capture endpoint, which is what says the two sides are two sides.
    ///
    /// Ids, not names: a headset appears on both lists with the same friendly name and two different
    /// endpoint ids, so comparing names would report the two enumerations as overlapping when they
    /// never do.
    /// </summary>
    [Fact]
    public void TheTwoEnumerationsAreDisjoint()
    {
        IReadOnlyList<(string Id, string Name)> render = WasapiCapture.ActiveRenderEndpoints();
        IReadOnlyList<(string Id, string Name)> capture = WasapiCapture.ActiveCaptureEndpoints();

        output.WriteLine($"{render.Count} render endpoint(s), {capture.Count} capture endpoint(s)");

        if (render.Count == 0 || capture.Count == 0)
            return;

        Assert.Empty(render.Select(one => one.Id).Intersect(capture.Select(one => one.Id), StringComparer.Ordinal));
    }

    /// <summary>
    /// A capture opened with no side named is still the microphone, which is what keeps PP652's
    /// callers as they were.
    /// </summary>
    [Fact]
    public void TheDefaultSideIsStillTheMicrophone()
    {
        using var capture = new WasapiCapture(_ => { });

        Assert.Equal(CaptureSide.Microphone, capture.Side);

        if (capture.Start() is CaptureResult.NoDevice)
            return;

        Assert.Equal(CaptureSide.Microphone, capture.Side);
    }

    /// <summary>And a loopback capture that never started is Stopped, not silent.</summary>
    [Fact]
    public void AReferenceThatNeverStartedIsStopped()
    {
        using var capture = new WasapiCapture(_ => { });

        Assert.Equal(CaptureHealth.Stopped, capture.Health);
        Assert.Equal(TimeSpan.Zero, capture.RunningFor);
    }

    /// <summary>
    /// THE OTHER HALF OF THE CRITERION: with something playing, the reference delivers.
    ///
    /// Every test above is about a quiet machine, which is the state this line is most about and the
    /// one that proves nothing about the format. So this MAKES a sound - half a second of a tone,
    /// generated here and played through the default render endpoint - and reads it back off the
    /// same endpoint through the loopback client.
    ///
    /// What that shows is the whole of the format claim: the render endpoint's mix is 32-bit float
    /// at whatever rate the device runs, and what arrives at the sink is whole 960-byte units of
    /// one-channel 16-bit 48000 - the announced format, through the converter AUTOCONVERTPCM puts in
    /// front. A reference in the device's own units would be useless to a subtraction.
    ///
    /// A machine that cannot play - no endpoint, or a host with no audio stack at all - reports and
    /// returns, the same shape every device test here has.
    /// </summary>
    [Fact]
    public void WithSomethingPlayingTheReferenceDeliversAnnouncedUnits()
    {
        if (WasapiCapture.ActiveRenderEndpoints().Count == 0)
            return;

        int expected = MicrophoneFormat.BytesPerUnit(MicrophoneFormat.Announced);
        var units = 0;
        var wrongSize = 0;

        using var listening = new WasapiCapture(one =>
        {
            if (one.Length != expected)
                Interlocked.Increment(ref wrongSize);

            Interlocked.Increment(ref units);
        });

        if (listening.Start(CaptureSide.RenderLoopback) is not CaptureResult.Running)
        {
            output.WriteLine($"the reference would not open: 0x{listening.LastError:x8}");
            return;
        }

        try
        {
            using var wav = new MemoryStream(Tone(TimeSpan.FromMilliseconds(500)));
            using var player = new System.Media.SoundPlayer(wav);
            player.PlaySync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.IO.FileNotFoundException)
        {
            output.WriteLine($"this machine cannot play: {error.Message}");
            return;
        }

        // The engine buffers, so the last packets arrive after the sound has finished.
        SpinWait.SpinUntil(() => Volatile.Read(ref units) >= 1, TimeSpan.FromSeconds(2));

        output.WriteLine(
            $"{Volatile.Read(ref units)} unit(s) heard back, {Volatile.Read(ref wrongSize)} of the wrong size, "
                + $"health {listening.Health}");

        Assert.True(
            Volatile.Read(ref units) >= 1,
            "the reference heard nothing while this test was playing a tone through the endpoint it reads");

        Assert.Equal(0, Volatile.Read(ref wrongSize));
        Assert.Equal(CaptureHealth.Streaming, listening.Health);
    }

    /// <summary>
    /// A RIFF WAV of a tone, in the announced format so the endpoint's own conversion is the only
    /// one between it and the reference.
    /// </summary>
    private static byte[] Tone(TimeSpan length)
    {
        MicrophoneAnnouncement announced = MicrophoneFormat.Announced;

        int frames = (int)(announced.Rate * length.TotalSeconds);
        int dataBytes = frames * announced.Channels * (announced.Bits / 8);

        var stream = new MemoryStream(44 + dataBytes);
        var write = new BinaryWriter(stream);

        write.Write("RIFF"u8);
        write.Write(36 + dataBytes);
        write.Write("WAVE"u8);
        write.Write("fmt "u8);
        write.Write(16);
        write.Write((short)1);
        write.Write((short)announced.Channels);
        write.Write(announced.Rate);
        write.Write(announced.Rate * announced.Channels * (announced.Bits / 8));
        write.Write((short)(announced.Channels * (announced.Bits / 8)));
        write.Write((short)announced.Bits);
        write.Write("data"u8);
        write.Write(dataBytes);

        for (var i = 0; i < frames; i++)
        {
            // Quiet on purpose - loud enough that the mix is not flagged silent, soft enough that a
            // suite run does not startle anybody.
            double v = 0.08 * Math.Sin(2 * Math.PI * 440 * i / announced.Rate);

            for (var c = 0; c < announced.Channels; c++)
                write.Write((short)(v * short.MaxValue));
        }

        write.Flush();
        return stream.ToArray();
    }
}
