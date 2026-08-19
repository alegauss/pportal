using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// The CHIAKI_LOG_* bits, which are a mask and not an ordering: a log lets through the levels
/// whose bits are set, so "info and worse" is three bits and not a comparison.
/// </summary>
[Flags]
public enum ChiakiLogLevel
{
    Error = 1 << 0,
    Warning = 1 << 1,
    Info = 1 << 2,
    Verbose = 1 << 3,
    Debug = 1 << 4,
    All = (1 << 5) - 1,
}

/// <summary>
/// PP4: a native callback arriving at a managed handler, which is the half of the seam the first
/// crossing did not prove.
///
/// libchiaki takes 22 callbacks and every one of them has this shape - a function pointer and a
/// <c>void *user</c> handed over once, called back later from whichever thread the library is on.
/// The log is the cheapest of them and the first one a session is handed, so it is where the
/// three questions get answered once for all the rest.
///
/// Where the function pointer comes from
/// -------------------------------------
/// <see cref="Dispatch"/> is <c>[UnmanagedCallersOnly]</c>, so <c>&amp;Dispatch</c> is the address
/// of a real native thunk rather than a managed delegate's. The delegate form works too and is
/// what most examples show, but it has a trap this one does not: the marshalled stub lives only as
/// long as the delegate object, and a delegate that was only ever passed to native code has no
/// managed reference left, so the collection that invalidates it happens whenever the GC next
/// runs. The symptom is a crash minutes into a session at a callback that worked a thousand times.
///
/// Where the state comes from
/// --------------------------
/// A static thunk has no <c>this</c>, so the instance travels as the <c>user</c> pointer libchiaki
/// hands back. It cannot be the object's address - the GC moves that - so it is a
/// <see cref="GCHandle"/>, whose integer is stable while the object it names is free to be
/// compacted. That is the whole answer to "without the GC moving it", and it is asserted with a
/// forced collection between the registration and the call rather than asserted by reasoning.
///
/// Why there is no finalizer
/// -------------------------
/// The handle is strong, so it roots this object: an instance that is never disposed is never
/// collected, and a finalizer would therefore never run. That is not an oversight to be tidied up
/// with <c>GCHandle.Alloc(..., Weak)</c> either - a weak handle would let the target go while
/// libchiaki still holds the pointer, which is the bug this class exists to not have.
/// <see cref="Dispose"/> is the only way out, and the native log outlives nothing that uses it.
/// </summary>
public sealed unsafe class ChiakiLog : IDisposable
{
    private readonly Action<ChiakiLogLevel, string> _sink;
    private GCHandle _self;
    private IntPtr _handle;

    /// <param name="mask">Which levels reach <paramref name="sink"/>; the rest are dropped in C.</param>
    /// <param name="sink">Called on whatever thread libchiaki logged from, never on a lock held here.</param>
    public ChiakiLog(ChiakiLogLevel mask, Action<ChiakiLogLevel, string> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;

        // Allocated before the native call, so the pointer handed over is already valid when the
        // library is free to call back - which, for the callbacks above this one, it is.
        _self = GCHandle.Alloc(this);
        _handle = LogCreate((uint)mask, &Dispatch, GCHandle.ToIntPtr(_self));
        if (_handle == IntPtr.Zero)
        {
            _self.Free();
            throw new OutOfMemoryException("chiaki_shim_log_create returned null.");
        }
    }

    /// <summary>The opaque native log, which is what a session function will be handed next.</summary>
    public IntPtr Handle => _handle;

    /// <summary>Read back out of C rather than remembered here, so the filter is asserted and not assumed.</summary>
    public ChiakiLogLevel LevelMask
    {
        get => (ChiakiLogLevel)LogLevelMask(Check());
        set => LogSetLevel(Check(), (uint)value);
    }

    /// <summary>
    /// One line into the same log libchiaki writes to, through the same filter and formatting, so
    /// the port's own messages interleave with the library's in the order they happened.
    /// </summary>
    public void Write(ChiakiLogLevel level, string message)
        => LogWrite(Check(), (int)level, message);

    /// <summary>The letter that level is written with: 'I', 'W', 'E', 'V', 'D', or '?'.</summary>
    public static char LevelChar(ChiakiLogLevel level) => LogLevelChar((int)level);

    public void Dispose()
    {
        // Native first: after this returns nothing can reach the dispatcher, so freeing the
        // handle afterwards cannot race a callback that is already inside it.
        if (_handle != IntPtr.Zero)
        {
            LogFree(_handle);
            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    private IntPtr Check()
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(ChiakiLog));

    /// <summary>
    /// The thunk libchiaki calls. Nothing may escape it: the frame above is C, and an exception
    /// crossing that boundary is a process abort with a stack that names neither side. A sink that
    /// throws loses its line, which is a worse log and not a dead client.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispatch(int level, IntPtr msg, IntPtr user)
    {
        try
        {
            if (user == IntPtr.Zero)
                return;
            if (GCHandle.FromIntPtr(user).Target is not ChiakiLog self)
                return;

            self._sink((ChiakiLogLevel)level, Marshal.PtrToStringUTF8(msg) ?? string.Empty);
        }
        catch
        {
            // Deliberately silent. There is nowhere to report to: the caller is C and the only
            // channel out of here is the sink that just failed.
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_log_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LogCreate(
        uint levelMask, delegate* unmanaged[Cdecl]<int, IntPtr, IntPtr, void> cb, IntPtr user);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_log_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void LogFree(IntPtr log);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_log_set_level",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void LogSetLevel(IntPtr log, uint levelMask);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_log_level_mask",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint LogLevelMask(IntPtr log);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_log_write",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void LogWrite(
        IntPtr log, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_log_level_char",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern byte LogLevelCharRaw(int level);

    private static char LogLevelChar(int level) => (char)LogLevelCharRaw(level);
}
