using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP785: the shape question for takion's oracles, asked of the build.
///
/// PP780 asked the linker what PP27's end state costs - takion.c, takionsendbuffer.c and
/// reorderqueue.c leaving the build - and the answer named this shim as the largest consumer by a
/// long way: eighteen of the twenty-four undefined symbols, twice what lib's own four files hold
/// between them. Those exports are the second opinion. A managed reorder queue, send buffer,
/// congestion formatter, MAC gate and AV parser are each held against the C they replace, so the
/// deletion removes what proves the replacement right at the same moment it removes the thing.
///
/// THE QUESTION IS ASKED OF THE DLL, NOT THE FILE, which is PP661's lesson and PP670's shape one
/// oracle over. The deletion leaves every declaration in the header inside its #ifdef, so a reader
/// keyed on the text says "wrapping" of a build that exports none of them - and 128 assertions went
/// red at once the last time a guard answered from something other than the built DLL.
///
/// WHAT MUST NOT HAPPEN IS A CHECK THAT STOPS ASKING (PP630). A guard that returned early on a tree
/// that HAS the oracle is a pass that measured nothing, so this is paired the way
/// <see cref="ShimFramePathShape"/> is: exactly one of the two sides answers on any tree, and the
/// bare side asserts the exports are really gone rather than assuming it.
///
/// AND THE DIFFERENTIAL DOES NOT SIMPLY DECLINE. That is the difference from the frame path, and it
/// is PP33's answer rather than a new one: <see cref="TakionOracleRecording"/> is what the C said,
/// taken from the library while it was here, so the comparison outlives it. What declines when the
/// oracle goes is the LIVE second opinion; the recorded one keeps running on every build.
/// </summary>
public static class ShimTakionShape
{
    /// <summary>The contract both shapes are about.</summary>
    public const string HeaderRelativePath = @"shim\chiaki_shim.h";

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>The header's text, or null outside a checkout.</summary>
    public static string? Read() => Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// Which shape the BUILT shim is in.
    ///
    /// A shim older than PP785 does not export the question, and the only build that predates it is
    /// one that has the wrappers - takion.c has never left this tree. No shim at all is bare:
    /// nothing it would wrap is reachable either way.
    /// </summary>
    public static ShimShape OfTheBuild()
    {
        try
        {
            return HasTakion() ? ShimShape.Wrapping : ShimShape.Bare;
        }
        catch (DllNotFoundException)
        {
            return ShimShape.Bare;
        }
        catch (EntryPointNotFoundException)
        {
            return ShimShape.Wrapping;
        }
    }

    /// <summary>Whether a differential may call into the C, which is the guard every one of them asks.</summary>
    public static bool OracleIsAvailable() => OfTheBuild() == ShimShape.Wrapping;

    [System.Runtime.InteropServices.DllImport(
        Native.ChiakiNative.Library, EntryPoint = "chiaki_shim_has_takion",
        CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
    private static extern bool HasTakion();

    /// <summary>The header while the build still carries the oracles, or null.</summary>
    public static string? WrappingHeader()
    {
        string? header = Read();
        return header is not null && OfTheBuild() == ShimShape.Wrapping ? header : null;
    }

    /// <summary>
    /// The header once they are gone, or null.
    ///
    /// The counterpart, and the reason a guard is not a way of not looking: what runs on this side
    /// is what says the deletion actually happened.
    /// </summary>
    public static string? BareHeader()
    {
        string? header = Read();
        return header is not null && OfTheBuild() == ShimShape.Bare ? header : null;
    }

    /// <summary>Whether exactly one of the two answers on this tree.</summary>
    public static bool ExactlyOneShapeAnswers()
        => Locate() is null || (WrappingHeader() is null) != (BareHeader() is null);

    /// <summary>
    /// What leaves the DLL with the three files: the shim exports over takion.c, takionsendbuffer.c
    /// and reorderqueue.c, by their SHIM names.
    ///
    /// The set <see cref="NativeSeam"/> will be allowed to find undefined once the shape is bare, so
    /// it has to be exactly the exports the deletion removes and not one more: an import allowed here
    /// that the deletion leaves alone is an import the census stopped checking for no reason.
    ///
    /// NOT THE SAME LIST AS PP780'S EIGHTEEN, and the difference is the direction. That census names
    /// the C symbols the linker reports; this names the shim exports that call them, which is what a
    /// managed import asks for. One shim export may reach two C symbols and one C symbol may be
    /// reached by three exports, so neither list is derivable from the other by counting.
    /// </summary>
    public static IReadOnlyList<string> GoneWhenBare { get; } =
    [
        // takionsendbuffer.c
        "chiaki_shim_takion_send_buffer_create",
        "chiaki_shim_takion_send_buffer_free",
        "chiaki_shim_takion_send_buffer_push",
        "chiaki_shim_takion_send_buffer_ack",
        "chiaki_shim_takion_send_buffer_count",

        // reorderqueue.c. The four the header defines inline go too, because the wrapper that calls
        // one is compiled into this DLL and the queue it walks is the C's own object.
        "chiaki_shim_reorder_queue_create_16",
        "chiaki_shim_reorder_queue_create_32",
        "chiaki_shim_reorder_queue_free",
        "chiaki_shim_reorder_queue_set_drop_strategy",
        "chiaki_shim_reorder_queue_size",
        "chiaki_shim_reorder_queue_count",
        "chiaki_shim_reorder_queue_push",
        "chiaki_shim_reorder_queue_pull",
        "chiaki_shim_reorder_queue_peek",
        "chiaki_shim_reorder_queue_drop",

        // takion.c: the congestion formatter, the MAC gate, both AV parsers and the loopback client.
        "chiaki_shim_takion_format_congestion",
        "chiaki_shim_takion_congestion_packet_size",
        "chiaki_shim_takion_packet_mac",
        "chiaki_shim_takion_v9_av_packet_parse",
        "chiaki_shim_takion_v7_av_packet_parse",
        "chiaki_shim_takion_v7_av_packet_format_header",
        "chiaki_shim_takion_connect_loopback",
        "chiaki_shim_takion_connected",
        "chiaki_shim_takion_event_count",
        "chiaki_shim_takion_close",
    ];

    /// <summary>Whatever of them a header's text still declares.</summary>
    public static IReadOnlyList<string> StillDeclaredIn(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return [.. GoneWhenBare.Where(name => header.Contains(name, StringComparison.Ordinal))];
    }
}
