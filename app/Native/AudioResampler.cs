namespace ChiakiNg.Native;

/// <summary>What one pass through the resampler did.</summary>
/// <param name="Fed">Bytes handed in.</param>
/// <param name="Produced">Bytes that came back. Zero is a legitimate answer while it fills.</param>
public readonly record struct ResamplePass(int Fed, int Produced);

/// <summary>
/// PP710, under PP52: Windows's own resampler, which is the bridge PP709's reading made necessary.
///
/// PP709 asked the Voice Capture DSP which output rates it takes and it answered 22050 and below,
/// refusing the 48000 streamconnection.c announces. So a cleaning stage cannot sit between PP652's
/// capture and PP694's encoder without something changing rate on the way out.
///
/// THE WAY IN IS FREE AND THE WAY OUT IS NOT. WasapiCapture already converts - AUTOCONVERTPCM puts
/// the engine's resampler in front, so asking either endpoint for a rate the canceller takes costs
/// no code at all. What has no answer is the return: the cleaned stream has to reach the rate the
/// console was told about, on a path with no engine in it.
///
/// SO IT IS THE ONE IN THE BOX. CLSID_CResamplerMediaObject is a DMO with one input and one output,
/// and <see cref="Dmo"/> is the plumbing PP709 already wrote for the canceller. Writing a resampler
/// instead would be a filter design this port has no reason to own, and shipping one would be a
/// dependency PP32 spent a task removing the case for.
///
/// A PASS CAN PRODUCE NOTHING, and that is not an error. A resampler holds samples back while its
/// filter fills, so the first calls answer zero and the count catches up - which is why this reports
/// what came back rather than asserting that something did.
/// </summary>
public sealed class AudioResampler : IDisposable
{
    /// <summary>CLSID_CResamplerMediaObject, the Audio Resampler DSP.</summary>
    public const string ResamplerClsid = "{f447b69e-1884-4a7e-8055-346f74d6edb3}";

    /// <summary>One in.</summary>
    public const int InputStreams = 1;

    /// <summary>One out.</summary>
    public const int OutputStreams = 1;

    private object? instance;
    private Dmo.IMediaObject? media;
    private bool configured;

    /// <summary>What it takes, or zero before <see cref="Configure"/>.</summary>
    public int FromRate { get; private set; }

    /// <summary>And what it produces.</summary>
    public int ToRate { get; private set; }

    /// <summary>What last refused, or zero.</summary>
    public int LastError { get; private set; }

    /// <summary>Whether the object exists at all.</summary>
    public bool Created => media is not null;

    /// <summary>Create the transform. A machine without it answers false rather than throwing.</summary>
    public bool Create()
    {
        if (media is not null)
            return true;

        instance = Dmo.Create(new Guid(ResamplerClsid), out int hresult);
        LastError = hresult;

        if (instance is not Dmo.IMediaObject asMedia)
        {
            Release();
            return false;
        }

        media = asMedia;
        return true;
    }

    /// <summary>The stream counts, which are one and one for this transform.</summary>
    public (int Inputs, int Outputs) StreamCounts()
    {
        if (media is not { } transform || transform.GetStreamCount(out int inputs, out int outputs) != 0)
            return (0, 0);

        return (inputs, outputs);
    }

    /// <summary>
    /// Point it from one rate to another, both 16-bit mono PCM.
    ///
    /// The input type goes first because the output's legality depends on it: a resampler with no
    /// input has nothing to convert FROM, and setting the pair the other way round is refused with
    /// an HRESULT that names neither.
    /// </summary>
    public bool Configure(int fromRate, int toRate)
    {
        if (media is not { } transform)
            return false;

        IntPtr input = Dmo.MediaType(fromRate);
        try
        {
            LastError = transform.SetInputType(0, input, 0);
            if (LastError != 0)
                return false;
        }
        finally
        {
            Dmo.FreeMediaType(input);
        }

        IntPtr output = Dmo.MediaType(toRate);
        try
        {
            LastError = transform.SetOutputType(0, output, 0);
            if (LastError != 0)
                return false;
        }
        finally
        {
            Dmo.FreeMediaType(output);
        }

        LastError = transform.AllocateStreamingResources();
        if (LastError != 0)
            return false;

        FromRate = fromRate;
        ToRate = toRate;
        configured = true;
        return true;
    }

    /// <summary>
    /// One pass: bytes at <see cref="FromRate"/> in, whatever is ready at <see cref="ToRate"/> out.
    /// </summary>
    /// <param name="from">16-bit mono PCM at the input rate.</param>
    /// <param name="into">Where the converted samples go; its length is the maximum taken.</param>
    public ResamplePass Process(ReadOnlySpan<byte> from, Span<byte> into)
    {
        if (media is not { } transform || !configured)
            return default;

        using var input = new Dmo.Buffer(from.Length);
        using var output = new Dmo.Buffer(into.Length);

        input.Fill(from);

        LastError = transform.ProcessInput(0, input, Dmo.InputSyncPoint, 0, 0);
        if (LastError < 0)
            return new ResamplePass(from.Length, 0);

        var buffers = new Dmo.OutputDataBuffer[1];
        buffers[0].Buffer = output;

        LastError = transform.ProcessOutput(0, 1, buffers, out _);
        if (LastError < 0)
            return new ResamplePass(from.Length, 0);

        return new ResamplePass(from.Length, output.Read(into));
    }

    public void Dispose() => Release();

    private void Release()
    {
        if (media is not null && configured)
            media.FreeStreamingResources();

        media = null;
        configured = false;
        FromRate = 0;
        ToRate = 0;

        if (instance is not null)
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(instance);
            instance = null;
        }
    }
}
