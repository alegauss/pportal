using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP670: the shape question for the frame path's oracles, asked of the build - PP662's move one
/// seam along.
///
/// The shim wrapped fec.c, frameprocessor.c and videoreceiver.c as fourteen exports so PP286
/// through PP291 could hold a managed port against the C it replaces. Those are oracles: they exist
/// for a differential to run, and the differentials were in six test files that called them
/// unguarded. The flip that took the four files out of the build (PP295's fourth criterion) put the
/// fourteen inside an #ifdef, and PP662 had measured what that does to an unguarded caller - 128
/// assertions red, every one of them a test with no way to ask which build it got.
///
/// PP697: THE FLIP HAS HAPPENED, so this file's tense has turned. PP696 took the four files out and
/// the option is off, which means the wrapping side is now the one nobody builds by default - and
/// everything below is written to work either way, because that is what it was for.
///
/// THE QUESTION IS ASKED OF THE DLL, NOT THE FILE, and that is the whole of PP661's lesson. The
/// flip leaves the declarations in the header text - inside the #ifdef - so a reader keyed on the
/// text says "wrapping" of a build that exports none of them. chiaki_shim_has_framepath is exported
/// whichever way the option goes and answers for the shim the host actually loaded. There is no
/// text-keyed <c>Of(header)</c> here on purpose: <see cref="ShimHolepunchShape.Of"/> is the reader
/// that was wrong the first time, kept there as the record.
///
/// WHAT MUST NOT HAPPEN IS A CHECK THAT STOPS ASKING (PP630). A guard that returns early on a tree
/// that has the oracle is a pass that measured nothing, so <see cref="WrappingHeader"/> is paired
/// with <see cref="BareHeader"/> and exactly one answers on any tree: the differentials run on one
/// side, and on the other a test asserts the exports are really gone - not from the text, from the
/// DLL, by calling one and catching what the loader says.
///
/// THE FLIP WAS NOT THIS FILE'S. PP623's order is two-state first, flip second, prose third, and
/// this was the first: the define was unconditional and the export said so, so every differential
/// could ask before the answer changed. PP696 was the second - the CMake line that put the define
/// behind an option, and the #ifdefs around the fourteen in the header and the body - and it edited
/// nothing here, which is what the first step is for.
/// </summary>
public static class ShimFramePathShape
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
    /// A shim older than PP670 does not export the question, and the only build that predates it
    /// is one that has the wrappers. No shim at all is bare: nothing it would wrap is reachable.
    /// </summary>
    public static ShimShape OfTheBuild()
    {
        try
        {
            return HasFramePath() ? ShimShape.Wrapping : ShimShape.Bare;
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

    [System.Runtime.InteropServices.DllImport(
        Native.ChiakiNative.Library, EntryPoint = "chiaki_shim_has_framepath",
        CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
    private static extern bool HasFramePath();

    /// <summary>
    /// The header while the build still carries the oracles, or null - which a differential reads
    /// as "not for me to run".
    /// </summary>
    public static string? WrappingHeader()
    {
        string? header = Read();
        return header is not null && OfTheBuild() == ShimShape.Wrapping ? header : null;
    }

    /// <summary>
    /// The header once they are gone, or null.
    ///
    /// The counterpart, and the reason a guard is not a way of not looking: what runs on this side
    /// is what says the flip actually happened.
    /// </summary>
    public static string? BareHeader()
    {
        string? header = Read();
        return header is not null && OfTheBuild() == ShimShape.Bare ? header : null;
    }

    /// <summary>
    /// Whether exactly one of the two answers on this tree.
    ///
    /// Two answers is a shape nothing modelled; none is every check on both sides declining while
    /// the wrappers sit there.
    /// </summary>
    public static bool ExactlyOneShapeAnswers()
        => Locate() is null || (WrappingHeader() is null) != (BareHeader() is null);

    /// <summary>
    /// What left the DLL with the four files: the fourteen shim exports over fec.c,
    /// frameprocessor.c and videoreceiver.c, by their SHIM names.
    ///
    /// This is the set <see cref="NativeSeam"/> allows to be undefined while the shape is bare, so
    /// it has to be exactly the exports the flip removed and not one more: an import allowed here
    /// that the flip left alone is an import the census stopped checking for no reason.
    /// </summary>
    public static IReadOnlyList<string> GoneWhenBare { get; } =
    [
        "chiaki_shim_fec_decode",
        "chiaki_shim_fec_matrix",
        "chiaki_shim_frame_processor_create",
        "chiaki_shim_frame_processor_free",
        "chiaki_shim_frame_processor_alloc_frame",
        "chiaki_shim_frame_processor_put_unit",
        "chiaki_shim_frame_processor_flush_possible",
        "chiaki_shim_frame_processor_flush",
        "chiaki_shim_frame_processor_stage_samples",
        "chiaki_shim_video_receiver_create",
        "chiaki_shim_video_receiver_free",
        "chiaki_shim_video_receiver_stream_info",
        "chiaki_shim_video_receiver_av_packet",
        "chiaki_shim_video_receiver_frames_lost",
    ];

    /// <summary>Whatever of the fourteen a header's text still declares.</summary>
    public static IReadOnlyList<string> StillDeclaredIn(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return [.. GoneWhenBare.Where(name => header.Contains(name, StringComparison.Ordinal))];
    }
}
