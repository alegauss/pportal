using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP694: what opusencoder.c does to a frame, through the shim, as the oracle.
///
/// NOT chiaki_opus_encoder_frame. That one needs an audio sender, which needs a ChiakiSession,
/// which needs a console - so the reachable half is opus_encode with the module's own two
/// parameters, and <see cref="OpusEncoderSource"/> holds the rest of the module against its file.
///
/// The application mode crosses rather than being written down twice: it is libopus's constant and
/// the managed encoder names Concentus's enum for it, so the two are held together by asking here.
/// </summary>
public sealed class NativeOpusEncoder : IDisposable
{
    private IntPtr handle;

    /// <summary>Creates one, or throws with the code libopus refused it by.</summary>
    public NativeOpusEncoder(int rate, int channels)
    {
        handle = EncoderCreate(rate, channels, out int error);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"opus_encoder_create failed: {error}.");
    }

    /// <summary>OPUS_APPLICATION_RESTRICTED_LOWDELAY, as libopus numbers it.</summary>
    public static int Application => EncoderApplication();

    /// <summary>Whether this build carries libopus and the five wrappers with it.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return HasOpus();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>opus_encode, with the return code handed back exactly as the C reads it.</summary>
    /// <param name="pcm">Interleaved 16-bit samples: frame size times channels.</param>
    /// <param name="frameSize">Samples per channel, which is what the C takes from the header.</param>
    /// <param name="into">The output buffer; its length is the maximum, as opusencoder.c passes it.</param>
    /// <returns>Bytes written, or a negative libopus error.</returns>
    public unsafe int Encode(ReadOnlySpan<short> pcm, int frameSize, Span<byte> into)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);

        fixed (short* samples = pcm)
        fixed (byte* output = into)
        {
            return Encode(handle, samples, frameSize, output, into.Length);
        }
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        EncoderDestroy(handle);
        handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_has_opus",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HasOpus();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_encoder_application",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int EncoderApplication();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_encoder_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr EncoderCreate(int rate, int channels, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_encoder_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void EncoderDestroy(IntPtr encoder);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_opus_encode",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int Encode(
        IntPtr encoder, short* pcm, int frameSize, byte* into, int intoSize);
}
