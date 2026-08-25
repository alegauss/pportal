using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>One message as it crossed the seam, copied out of libchiaki's buffer.</summary>
/// <param name="Direction">Which way it went.</param>
/// <param name="Channel">"ctrl" or "session" - the conversation, not the socket.</param>
/// <param name="Type">The ctrl message type, or 0 where the channel is the type.</param>
/// <param name="Payload">The plaintext, copied. See <see cref="ChiakiMessageTap"/> for why copied.</param>
public readonly record struct TappedMessage(
    ExchangeTapDirection Direction, string Channel, ushort Type, byte[] Payload);

/// <summary>Which way a tapped message went. Matches ChiakiMessageTapDirection.</summary>
public enum ExchangeTapDirection
{
    Sent = 0,
    Received = 1,
}

/// <summary>
/// PP323: the plaintext of a session arriving in managed code, which is the source PP297 was
/// written as though it had.
///
/// PP297 needs a recorded exchange to port session.c, ctrl.c, streamconnection.c and senkusha.c
/// against - the four modules with no test at all - and named a console as the only thing missing.
/// It was not. What a managed caller could see was the log, and the log cannot be the source: the
/// session bytes reach it as a hexdump PP320 redacts WHOLE, because a formatted row cannot be
/// redacted by field without leaving the tail of a key on the next one, and ctrl logs a type and a
/// size and never a payload.
///
/// What arrives here instead is structured. A direction, a channel, a message type and the bytes -
/// so the sanitiser can name a field, which is the sentence PP297's own why ends on.
///
/// THE PAYLOAD IS COPIED, and that is the one decision in this class
/// -----------------------------------------------------------------
/// libchiaki's pointer is valid only for the duration of the call, and for the ctrl send site it is
/// worse than that: the buffer is encrypted IN PLACE one statement later. A handler that kept the
/// span would read ciphertext rather than crash - it would look like corruption in a recording and
/// not like a bug in a tap.
///
/// So the copy happens inside the thunk, before anything else can decide not to. It costs an
/// allocation per control message, which is a rate of a few per second on a live session and is not
/// the frame path: PP113's zero-allocation budget is about the transport, and a recording is a
/// diagnostic somebody turned on.
///
/// GLOBAL, because lib/src's tap is
/// --------------------------------
/// Three of the four emit sites are static functions with no handle a caller ever named. One tap is
/// installed at a time; a second <see cref="Install"/> replaces the first rather than adding to it,
/// which is stated here because the alternative - a silent list - is how two recordings become one
/// file nobody can split.
/// </summary>
public sealed unsafe class ChiakiMessageTap : IDisposable
{
    /// <summary>The one that is installed, or null. See the note on being global.</summary>
    private static ChiakiMessageTap? installed;

    private readonly Action<TappedMessage> sink;
    private GCHandle self;
    private bool disposed;

    private ChiakiMessageTap(Action<TappedMessage> sink)
    {
        this.sink = sink;
        self = GCHandle.Alloc(this);
    }

    /// <summary>
    /// Installs a tap, replacing whatever was installed before it.
    /// </summary>
    /// <param name="sink">
    /// Called on the ctrl thread or the session thread - threads the caller never created - so it
    /// must not touch a UI object and must not block. It is called with the payload already copied.
    /// </param>
    public static ChiakiMessageTap Install(Action<TappedMessage> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // The old one first. Two taps installed at once is not a thing lib/src can hold, and the
        // window where neither is installed is narrower than the window where the wrong one is.
        installed?.Dispose();

        var tap = new ChiakiMessageTap(sink);
        installed = tap;
        TapSet(&Dispatch, GCHandle.ToIntPtr(tap.self));
        return tap;
    }

    /// <summary>Whether lib/src is emitting, read out of C rather than remembered here.</summary>
    public static bool Active => TapActive();

    /// <summary>
    /// Emits one message the way a library site does - through lib/src's own emit, not straight to
    /// the sink.
    ///
    /// The same argument <c>chiaki_shim_log_write</c> makes: what a caller exercises has to be the
    /// one implementation the four sites use, or the thing under test is a second copy of it. It is
    /// also the only way this is checkable at all without a console reaching the stream, which is
    /// exactly what PP297 does not have.
    /// </summary>
    public static void Emit(ExchangeTapDirection direction, string channel, ushort type, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(channel);

        fixed (byte* bytes = payload)
            TapEmit((int)direction, channel, type, (IntPtr)bytes, payload.Length);
    }

    /// <summary>Uninstalls it. Idempotent, and a tap that is not the installed one only frees itself.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // Native first, and only where this one is still the installed tap: a later Install has
        // already replaced the pointer, and clearing it here would silently stop that one.
        if (ReferenceEquals(installed, this))
        {
            TapSet(null, IntPtr.Zero);
            installed = null;
        }

        if (self.IsAllocated)
            self.Free();
    }

    /// <summary>
    /// The thunk lib/src calls. Nothing may escape it: the frame above is C, and an exception
    /// crossing that boundary is a process abort with a stack that names neither side.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispatch(
        int direction, IntPtr channel, ushort type, IntPtr payload, int payloadSize, IntPtr user)
    {
        try
        {
            if (user == IntPtr.Zero)
                return;
            if (GCHandle.FromIntPtr(user).Target is not ChiakiMessageTap tap)
                return;

            // Copied HERE. Past this line the bytes are the runtime's, and the ctrl send site is
            // free to encrypt its buffer in place the way it is about to.
            byte[] bytes = payloadSize > 0 && payload != IntPtr.Zero
                ? new ReadOnlySpan<byte>((void*)payload, payloadSize).ToArray()
                : [];

            tap.sink(new TappedMessage(
                (ExchangeTapDirection)direction,
                Marshal.PtrToStringUTF8(channel) ?? string.Empty,
                type,
                bytes));
        }
        catch
        {
            // Deliberately silent, for ChiakiLog's reason: the caller is C and the only channel out
            // of here is the sink that just failed.
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_tap_set",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TapSet(
        delegate* unmanaged[Cdecl]<int, IntPtr, ushort, IntPtr, int, IntPtr, void> cb, IntPtr user);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_tap_active",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool TapActive();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_tap_emit",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TapEmit(
        int direction, [MarshalAs(UnmanagedType.LPUTF8Str)] string channel, ushort type,
        IntPtr payload, int payloadSize);
}
