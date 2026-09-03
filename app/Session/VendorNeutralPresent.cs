namespace ChiakiNg.Session;

/// <summary>
/// PP647: the present path names no GPU vendor, held against the source rather than asserted.
///
/// Block I is titled "NVIDIA path" and it is a SCHEDULE, not a taxonomy - roadkeep.toml's ordering
/// note is what the priority queue reads, and "Block I" there means the push that was put second on
/// instruction, not a claim that every line under it needs a particular card. Two of its lines were
/// never image quality and PP53 is one: tearing is DXGI, and DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING is
/// how an application asks any adaptive-sync display to show a frame when it arrives. FreeSync and
/// VESA Adaptive-Sync answer to the same pair.
///
/// A heading cannot be relied on to say that, and this port declared a non-goal that makes the
/// misreading expensive: "no vendor path whose absence is visible to the user" binds a proposal to
/// the floor in docs/HARDWARE-CONTRACT.md, whose whole argument is that a machine with Intel
/// graphics is an ordinary laptop. A latency win filed under a vendor heading reads as gated, and
/// gets scheduled behind hardware it does not need.
///
/// So the claim is checked where it can be: every file of the render shim - the swapchain probes,
/// the DirectComposition trees and the tearing probes together - names no vendor at all. The day
/// somebody reaches for NVAPI to do this, the check goes red and the contract's row is re-read
/// rather than quietly outlived.
/// </summary>
public static class VendorNeutralPresent
{
    /// <summary>
    /// The render shim: everything this port has built that puts a frame on a display.
    ///
    /// All three, not the tearing probe alone. The claim worth holding is about the present PATH,
    /// and a check narrowed to one function would pass on a file that reached for NVAPI two
    /// declarations above it.
    /// </summary>
    public static IReadOnlyList<string> PresentPathFiles { get; } =
    [
        @"shim\chiaki_render.h",
        @"shim\chiaki_render.c",
        @"shim\chiaki_render_dcomp.cpp",
    ];

    /// <summary>
    /// The one that DOES name a vendor, deliberately, and is the control for this check.
    ///
    /// PP77 lifted chiaki_decoder_choice out of a Qt method so the branch holding the non-NVIDIA
    /// floor could be asserted at all, and its signature takes an <c>nvidia_card</c> flag because
    /// the decision genuinely turns on one. That is the row the contract already covers with a
    /// test; this file is about the row beside it.
    /// </summary>
    public const string DecoderChoiceShim = @"shim\chiaki_shim.c";

    /// <summary>
    /// What counts as naming a vendor. Comments included on purpose: a path that explains itself in
    /// terms of one card is one somebody will implement in terms of that card.
    /// </summary>
    public static IReadOnlySet<string> VendorTokens { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nvapi", "nvidia", "geforce", "cuda", "nvenc", "nvdec",
            "amd_ags", "radeon", "adrenalin",
            "intel_", "quicksync",
        };

    /// <summary>A file of the shim, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Which vendor tokens a source names, in the order this class declares them.</summary>
    public static IReadOnlyList<string> VendorNamesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            .. VendorTokens
                .Where(token => source.Contains(token, StringComparison.OrdinalIgnoreCase))
                .OrderBy(token => token, StringComparer.Ordinal),
        ];
    }
}
