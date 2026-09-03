using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the two shapes the shim's holepunch seam is in.</summary>
public enum ShimShape
{
    /// <summary>It still declares the wrappers, which is every model's subject today.</summary>
    Wrapping,

    /// <summary>It declares none of them, which is what PP655's flip leaves behind.</summary>
    Bare,
}

/// <summary>
/// PP655's first step: the one question the shim's seam needs answered, asked once.
///
/// PP630 did this for session.c and PP631 converted ten models through it. This is the same
/// mechanism one layer along, for the seam PP653 found is all that holds holepunch.c in the build.
///
/// THE QUESTION IS THE HEADER, and that is the whole point rather than a convenience.
/// <see cref="NativeSeam"/> holds the host's DllImports against what the shim HEADERS declare,
/// because a header declaration is the contract and the definition's spelling is the compiler's
/// business. So the header is what a flip has to change, and a flip that changed only the bodies
/// would leave that census green while the DLL lost nine exports - the hazard PP655 named and the
/// one door PP437 cannot see. Keying this reader on the header makes the shape and the census agree
/// about what "gone" means.
///
/// WHAT MUST NOT HAPPEN IS A CHECK THAT STOPS ASKING, which is PP630's warning and it applies
/// unchanged. Every reader here returns null outside a checkout, and a shape guard bolted on
/// carelessly makes that same early return happen on a tree that IS one. So <see cref="WrappingHeader"/>
/// is paired with <see cref="BareHeader"/>: on any tree exactly one answers, and a model converted
/// through the pair has assertions running either way.
///
/// THE DEVICE ID IS NOT IN THIS SET, and the reason needed correcting. It was written here as "the
/// one wrapper whose absence would not be a change of shape", which reads as though it could stay.
/// It cannot: chiaki_holepunch_generate_client_device_uid is defined in holepunch.c, so it leaves
/// with the file like everything else. What is true is narrower and is why it is still excluded -
/// PP654 took it off every path the host runs, so its absence costs an ORACLE and not a feature,
/// and <see cref="GoneWhenBare"/> is the set whose absence is the seam changing rather than the set
/// of everything the flip removes.
/// </summary>
public static class ShimHolepunchShape
{
    /// <summary>The contract both shapes are about.</summary>
    public const string HeaderRelativePath = @"shim\chiaki_shim.h";

    /// <summary>
    /// The declaration the shape is keyed on.
    ///
    /// One name rather than nine, for PP630's reason: the flip is one commit, so no tree exists
    /// where some of them are declared and some are not. Session init is the one every other
    /// wrapper takes a handle from, which makes it the last one a partial removal would leave.
    /// </summary>
    public const string KeyDeclaration = "chiaki_shim_holepunch_session_init";

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>The header's text, or null outside a checkout.</summary>
    public static string? Read() => Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>Which shape a given header is in.</summary>
    public static ShimShape Of(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Contains(KeyDeclaration, StringComparison.Ordinal)
            ? ShimShape.Wrapping
            : ShimShape.Bare;
    }

    /// <summary>
    /// The header while it still declares the wrappers, or null - which a caller reads as "not for
    /// me to check".
    /// </summary>
    public static string? WrappingHeader()
    {
        string? header = Read();
        return header is not null && Of(header) == ShimShape.Wrapping ? header : null;
    }

    /// <summary>
    /// The header once they are gone, or null.
    ///
    /// The counterpart, and the reason the guard is not a way of not looking: what runs on this side
    /// is what says the flip actually happened.
    /// </summary>
    public static string? BareHeader()
    {
        string? header = Read();
        return header is not null && Of(header) == ShimShape.Bare ? header : null;
    }

    /// <summary>
    /// Whether exactly one of the two answers on this tree.
    ///
    /// Asserted rather than assumed, for PP630's reason: two answers is a shape nothing modelled,
    /// and none at all is every check on both sides declining while the file sits there.
    /// </summary>
    public static bool ExactlyOneShapeAnswers()
        => Locate() is null || (WrappingHeader() is null) != (BareHeader() is null);

    /// <summary>
    /// What must be gone from the bare shape.
    ///
    /// The nine that are PP481's oracle, by their SHIM names rather than the C symbols they call -
    /// the header is the subject, and it declares wrappers. The device id is absent from this list
    /// because PP654 already took it off the host's path; its wrapper may stay or go without the
    /// seam changing shape.
    /// </summary>
    public static IReadOnlyList<string> GoneWhenBare { get; } =
    [
        "chiaki_shim_holepunch_session_init",
        "chiaki_shim_holepunch_session_set_recorded",
        "chiaki_shim_holepunch_get_sock",
        "chiaki_shim_holepunch_get_regist_info",
        "chiaki_shim_holepunch_get_selected_addr",
        "chiaki_shim_holepunch_get_ctrl_port",
        "chiaki_shim_holepunch_create_offer",
        "chiaki_shim_holepunch_punch_hole",
        "chiaki_shim_holepunch_session_fini",
    ];

    /// <summary>
    /// The wrapper that also leaves, and whose absence costs an oracle rather than a shape.
    ///
    /// PP654's device id. Kept out of <see cref="GoneWhenBare"/> because the seam's shape is about
    /// the nine, and named here because a reader working out what the flip removes needs it - the C
    /// behind it is in holepunch.c and goes with the file.
    /// </summary>
    public const string OracleWrapper = "chiaki_shim_generate_client_device_uid";

    /// <summary>
    /// Whether the shim still offers the device id's C, which is what a check comparing the two
    /// implementations needs before it runs.
    /// </summary>
    public static bool TheFormatOracleIsAvailable()
        => Read() is { } header && header.Contains(OracleWrapper, StringComparison.Ordinal);

    /// <summary>Whatever is still declared that the bare shape must not have.</summary>
    public static IReadOnlyList<string> StillDeclaredIn(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return [.. GoneWhenBare.Where(name => header.Contains(name, StringComparison.Ordinal))];
    }
}
