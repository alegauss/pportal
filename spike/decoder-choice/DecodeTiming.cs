using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DecoderChoice;

/// <summary>What one timed decode came to.</summary>
/// <param name="Decoder">The transform's friendly name, so a result names what produced it.</param>
/// <param name="Frames">How many frames came out, after the warmup ones were discarded.</param>
/// <param name="Paced">Whether the input was fed at 60 fps or as fast as the decoder would take it.</param>
/// <param name="MeanMicroseconds">The mean per frame.</param>
/// <param name="P50Microseconds">The median, which is the number a distribution is described by.</param>
/// <param name="P99Microseconds">And the tail, which is where PP65 found a stall that a mean hid.</param>
internal readonly record struct DecodeReading(
    string Decoder, int Frames, bool Paced,
    double MeanMicroseconds, double P50Microseconds, double P99Microseconds);

/// <summary>
/// PP818: the decode PP650 did not time, on the terms the port would actually run it.
///
/// PP650 answered what Media Foundation offers and what leaving FFmpeg would cost, and its own
/// README states the gap in one line: no decode was timed here. The comparison table it prints is
/// spike/decode-path's, taken through FFmpeg, so the two halves of the choice were measured in
/// different units.
///
/// THE CONFIGURATION IS THE MEASUREMENT. A decode timed on the wrong terms answers about something
/// nobody would ship, so three things are fixed and none of them is optional:
///
///   the decoder is handed an IMFDXGIDeviceManager over a real D3D11 device, so what comes out is
///   a texture the renderer takes rather than a frame that travels through system memory - which is
///   the whole of what PP319's present path needs from a decoder, and the 2253 microseconds
///   spike/decode-path measured d3d11va paying is exactly the copy this avoids;
///
///   MF_LOW_LATENCY is set, which turns off the reordering a stream with no B-frames does not need
///   and a remote play client cannot afford;
///
///   and the input is PACED at 60 fps by default, because PP71 established that feeding a decoder
///   as fast as it will take it measures a machine no client runs on - it was the difference
///   between cuda looking best and cuda looking worst.
/// </summary>
internal static class DecodeTiming
{
    /// <summary>The interval this is paced at, which is spike/decode-path's 60 fps.</summary>
    private const double FrameIntervalMicroseconds = 1_000_000.0 / 60.0;

    /// <summary>Discarded before anything is counted, as spike/decode-path discards them.</summary>
    private const int WarmupFrames = 30;

    /// <summary>MFT_OUTPUT_STREAM_PROVIDES_SAMPLES.</summary>
    private const int ProvidesSamples = 0x00000100;

    private const int DriverTypeHardware = 1;
    private const int CreateDeviceVideoSupport = 0x00000800;
    private const int CreateDeviceBgraSupport = 0x00000020;
    private const int SdkVersion = 7;

    /// <summary>
    /// Decodes the stream and reports what each frame cost, or null where nothing could decode.
    /// </summary>
    /// <param name="streamPath">An Annex-B H.264 elementary stream.</param>
    /// <param name="width">The stream's width, which the input type has to carry.</param>
    /// <param name="height">And its height.</param>
    /// <param name="paced">Feed at 60 fps rather than as fast as the decoder accepts.</param>
    /// <param name="limit">Stop after this many access units; 0 is the whole file.</param>
    public static DecodeReading? Measure(
        string streamPath, int width, int height, bool paced, int limit)
    {
        if (!File.Exists(streamPath))
        {
            Console.Error.WriteLine(
                $"no stream at {streamPath} - spike/decode-path's run.cmd generates it, and this "
                    + "spike deliberately does not, because a second generator is a second stream.");
            return null;
        }

        IReadOnlyList<ArraySegment<byte>> units = AccessUnits.Split(File.ReadAllBytes(streamPath), limit);

        if (units.Count <= WarmupFrames)
        {
            Console.Error.WriteLine($"only {units.Count} access unit(s), which is not more than the warmup");
            return null;
        }

        Console.WriteLine($"{units.Count} access unit(s) parsed from {Path.GetFileName(streamPath)}");

        IntPtr device = IntPtr.Zero;
        Mf.IMFDXGIDeviceManager? manager = null;
        Mf.IMFTransform? decoder = null;
        string name = "(unnamed)";

        try
        {
            int made = Mf.D3D11CreateDevice(
                IntPtr.Zero, DriverTypeHardware, IntPtr.Zero,
                CreateDeviceVideoSupport | CreateDeviceBgraSupport,
                IntPtr.Zero, 0, SdkVersion, out device, out _, IntPtr.Zero);

            if (made != 0)
            {
                Console.Error.WriteLine($"D3D11CreateDevice failed with 0x{made:x8}");
                return null;
            }

            if (Mf.MFCreateDXGIDeviceManager(out uint token, out manager) != 0
                || manager.ResetDevice(device, token) != 0)
            {
                Console.Error.WriteLine("the DXGI device manager would not take the device");
                return null;
            }

            decoder = Activate(out name);

            if (decoder is null)
                return null;

            // The manager, BEFORE the types: a decoder told about D3D11 afterwards offers a
            // different set of output types, so the order here decides what SetOutputType can pick.
            if (!Configure(decoder, manager, width, height))
                return null;

            double[] costs = Run(decoder, units, paced);

            if (costs.Length == 0)
            {
                Console.Error.WriteLine("the decoder accepted every access unit and produced no frame");
                return null;
            }

            Array.Sort(costs);

            return new DecodeReading(
                name,
                costs.Length,
                paced,
                costs.Average(),
                costs[costs.Length / 2],
                costs[Math.Min(costs.Length - 1, (int)(costs.Length * 0.99))]);
        }
        finally
        {
            if (decoder is not null)
                Marshal.ReleaseComObject(decoder);

            if (manager is not null)
                Marshal.ReleaseComObject(manager);

            if (device != IntPtr.Zero)
                Marshal.Release(device);
        }
    }

    /// <summary>The first D3D11-aware H.264 decoder this machine registers.</summary>
    private static Mf.IMFTransform? Activate(out string name)
    {
        name = "(unnamed)";

        var input = new Mf.RegisterTypeInfo
        {
            MajorType = Mf.MediaTypeVideo,
            Subtype = Mf.VideoFormatH264,
        };

        const int flags = Mf.MftEnumFlagHardware | Mf.MftEnumFlagSyncMft | Mf.MftEnumFlagAsyncMft;

        if (Mf.MFTEnumEx(Mf.CategoryVideoDecoder, flags, ref input, IntPtr.Zero, out IntPtr array, out uint count)
            != 0)
        {
            Console.Error.WriteLine("MFTEnumEx found nothing for H.264");
            return null;
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                IntPtr entry = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                if (entry == IntPtr.Zero)
                    continue;

                object activation = Marshal.GetObjectForIUnknown(entry);
                Marshal.Release(entry);

                var activate = (Mf.IMFActivate)activation;
                Guid iid = Mf.IidTransform;

                if (activate.ActivateObject(ref iid, out object? made) == 0
                    && made is Mf.IMFTransform transform)
                {
                    Guid key = Mf.FriendlyNameAttribute;

                    if (activate.GetStringLength(ref key, out uint length) == 0)
                    {
                        var buffer = new char[length + 1];
                        if (activate.GetString(ref key, buffer, (uint)buffer.Length, out uint written) == 0)
                            name = new string(buffer, 0, (int)written);
                    }

                    return transform;
                }
            }
        }
        finally
        {
            Mf.CoTaskMemFree(array);
        }

        Console.Error.WriteLine("no H.264 decoder could be activated");
        return null;
    }

    /// <summary>The device manager, low latency, and the two media types.</summary>
    private static bool Configure(
        Mf.IMFTransform decoder, Mf.IMFDXGIDeviceManager manager, int width, int height)
    {
        IntPtr unknown = Marshal.GetIUnknownForObject(manager);

        try
        {
            int told = decoder.ProcessMessage(Mf.MessageSetD3DManager, unknown);

            if (told != 0)
            {
                Console.Error.WriteLine(
                    $"MFT_MESSAGE_SET_D3D_MANAGER refused with 0x{told:x8} - this decoder will not "
                        + "take a D3D11 device, so what follows would time a software decode.");
                return false;
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }

        if (decoder.GetAttributes(out Mf.IMFAttributes? own) == 0 && own is not null)
        {
            Guid latency = Mf.LowLatency;
            own.SetUINT32(ref latency, 1);
            Marshal.ReleaseComObject(own);
        }

        if (Mf.MFCreateMediaType(out Mf.IMFMediaType inputType) != 0)
            return false;

        try
        {
            Guid major = Mf.MtMajorType;
            Guid video = Mf.MediaTypeVideo;
            Guid subtypeKey = Mf.MtSubtype;
            Guid h264 = Mf.VideoFormatH264;
            Guid sizeKey = Mf.MtFrameSize;
            Guid rateKey = Mf.MtFrameRate;
            Guid interlace = Mf.MtInterlaceMode;

            inputType.SetGUID(ref major, ref video);
            inputType.SetGUID(ref subtypeKey, ref h264);
            inputType.SetUINT64(ref sizeKey, Pack((uint)width, (uint)height));
            inputType.SetUINT64(ref rateKey, Pack(60, 1));
            inputType.SetUINT32(ref interlace, Mf.InterlaceModeProgressive);

            int set = decoder.SetInputType(0, inputType, 0);

            if (set != 0)
            {
                Console.Error.WriteLine($"SetInputType failed with 0x{set:x8}");
                return false;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(inputType);
        }

        // NV12 among whatever it offers, rather than a type built here: a decoder's available list
        // is what it will actually produce, and constructing one that merely looks right is how a
        // SetOutputType comes back with a type mismatch nothing explains.
        for (uint index = 0; ; index++)
        {
            if (decoder.GetOutputAvailableType(0, index, out Mf.IMFMediaType? offered) != 0 || offered is null)
                break;

            try
            {
                Guid subtypeKey = Mf.MtSubtype;

                if (offered.GetGUID(ref subtypeKey, out Guid subtype) == 0 && subtype == Mf.VideoFormatNv12)
                {
                    int set = decoder.SetOutputType(0, offered, 0);

                    if (set == 0)
                        return true;

                    Console.Error.WriteLine($"SetOutputType for NV12 failed with 0x{set:x8}");
                    return false;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(offered);
            }
        }

        Console.Error.WriteLine("the decoder offered no NV12 output type");
        return false;
    }

    /// <summary>Feeds every access unit and times what each frame cost to come out.</summary>
    private static double[] Run(
        Mf.IMFTransform decoder, IReadOnlyList<ArraySegment<byte>> units, bool paced)
    {
        decoder.ProcessMessage(Mf.MessageNotifyBeginStreaming, IntPtr.Zero);
        decoder.ProcessMessage(Mf.MessageNotifyStartOfStream, IntPtr.Zero);

        decoder.GetOutputStreamInfo(0, out Mf.OutputStreamInfo info);
        bool decoderAllocates = (info.Flags & ProvidesSamples) != 0;

        var costs = new List<double>(units.Count);
        var clock = new Stopwatch();
        var pacer = Stopwatch.StartNew();
        var produced = 0;
        long duration = (long)(10_000_000.0 / 60.0);

        for (var i = 0; i < units.Count; i++)
        {
            if (paced)
            {
                double due = i * FrameIntervalMicroseconds;
                double wait = due - pacer.Elapsed.TotalMicroseconds;

                if (wait > 0)
                    Thread.Sleep(TimeSpan.FromMicroseconds(wait));
            }

            Mf.IMFSample sample = Wrap(units[i], i * duration, duration);

            clock.Restart();

            int fed = decoder.ProcessInput(0, sample, 0);

            Marshal.ReleaseComObject(sample);

            if (fed != 0)
            {
                clock.Stop();
                continue;
            }

            // Every frame this input made available. A decoder holding one back answers
            // NeedMoreInput, which is not an error and is where the loop ends.
            while (Drain(decoder, info, decoderAllocates))
            {
                double cost = clock.Elapsed.TotalMicroseconds;
                clock.Restart();

                produced++;

                if (produced > WarmupFrames)
                    costs.Add(cost);
            }

            clock.Stop();
        }

        decoder.ProcessMessage(Mf.MessageNotifyEndOfStream, IntPtr.Zero);
        decoder.ProcessMessage(Mf.MessageCommandDrain, IntPtr.Zero);

        while (Drain(decoder, info, decoderAllocates))
        {
            produced++;
        }

        Console.WriteLine($"{produced} frame(s) decoded, {costs.Count} counted after warmup");

        return [.. costs];
    }

    /// <summary>One ProcessOutput. True where a frame came out.</summary>
    private static bool Drain(Mf.IMFTransform decoder, Mf.OutputStreamInfo info, bool decoderAllocates)
    {
        var buffers = new Mf.OutputDataBuffer { StreamId = 0 };
        Mf.IMFSample? ours = null;

        if (!decoderAllocates)
        {
            if (Mf.MFCreateSample(out ours) != 0
                || Mf.MFCreateMemoryBuffer(Math.Max(info.Size, 1), out Mf.IMFMediaBuffer store) != 0)
            {
                return false;
            }

            ours.AddBuffer(store);
            Marshal.ReleaseComObject(store);
            buffers.Sample = Marshal.GetIUnknownForObject(ours);
        }

        try
        {
            int got = decoder.ProcessOutput(0, 1, ref buffers, out _);

            if (got == Mf.TransformStreamChange)
            {
                // The decoder has learned the stream's real format and wants the output type set
                // again. Not an error, and not a frame either.
                Renegotiate(decoder);
                return false;
            }

            if (got != 0)
                return false;

            // The frame itself is released and nothing is done with it. What is being timed is the
            // DECODE, which is spike/decode-path's own separation: a readback inside this loop
            // would make it impossible to say which call the hardware synchronised in.
            if (buffers.Sample != IntPtr.Zero && decoderAllocates)
                Marshal.Release(buffers.Sample);

            return true;
        }
        finally
        {
            if (ours is not null)
            {
                if (buffers.Sample != IntPtr.Zero)
                    Marshal.Release(buffers.Sample);

                Marshal.ReleaseComObject(ours);
            }
        }
    }

    /// <summary>Sets the output type again after the decoder reports a format change.</summary>
    private static void Renegotiate(Mf.IMFTransform decoder)
    {
        for (uint index = 0; ; index++)
        {
            if (decoder.GetOutputAvailableType(0, index, out Mf.IMFMediaType? offered) != 0 || offered is null)
                return;

            try
            {
                Guid subtypeKey = Mf.MtSubtype;

                if (offered.GetGUID(ref subtypeKey, out Guid subtype) == 0
                    && subtype == Mf.VideoFormatNv12
                    && decoder.SetOutputType(0, offered, 0) == 0)
                {
                    return;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(offered);
            }
        }
    }

    /// <summary>One access unit as a sample the transform will take.</summary>
    private static Mf.IMFSample Wrap(ArraySegment<byte> unit, long time, long duration)
    {
        Mf.MFCreateSample(out Mf.IMFSample sample);
        Mf.MFCreateMemoryBuffer(unit.Count, out Mf.IMFMediaBuffer buffer);

        buffer.Lock(out IntPtr target, out _, out _);
        Marshal.Copy(unit.Array!, unit.Offset, target, unit.Count);
        buffer.Unlock();
        buffer.SetCurrentLength(unit.Count);

        sample.AddBuffer(buffer);
        sample.SetSampleTime(time);
        sample.SetSampleDuration(duration);

        Marshal.ReleaseComObject(buffer);

        return sample;
    }

    /// <summary>Two 32-bit halves in one 64-bit attribute, high word first. MFSetAttributeSize.</summary>
    private static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;
}
